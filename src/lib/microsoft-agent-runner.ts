import { InMemoryAgentRunner, InMemoryThread } from "@copilotkit/runtime/v2";
import type { AgentRunnerRunRequest, AgentRunnerConnectRequest } from "@copilotkit/runtime/v2";
import { tap, finalize, map } from "rxjs/operators";

/**
 * Custom AgentRunner that persists threads to Microsoft Agent Framework
 * 
 * Extends InMemoryAgentRunner to keep in-memory streaming functionality
 * while adding persistence to Postgres via Microsoft Agent Framework API
 */

const AGENT_URL = process.env.AGENT_URL || "http://localhost:8000";

export class MicrosoftAgentRunner extends InMemoryAgentRunner {
  /**
   * Override listThreads() to fetch from Microsoft Agent Framework
   * This is called by runtime when handling GET /threads
   */
  override listThreads(): InMemoryThread[] {
    // This method runs synchronously but we need to fetch from external API
    // We'll use the parent's in-memory threads as cache
    // and sync them in background
    const threads = super.listThreads();
    console.log(`[MicrosoftAgentRunner] listThreads() called, returning ${threads.length} threads`);
    return threads;
  }
  
  /**
   * Async version to fetch threads from Microsoft Agent Framework
   * Called periodically to sync with database
   */
  async syncThreadsFromDatabase(agentId: string): Promise<InMemoryThread[]> {
    try {
      const response = await fetch(`${AGENT_URL}/api/threads?agentId=${agentId}`);
      
      if (!response.ok) {
        console.error(`[MicrosoftAgentRunner] Failed to fetch threads: ${response.status}`);
        return [];
      }
      
      const threads = await response.json();
      
      // Transform to InMemoryThread format
      return threads.map((t: any) => ({
        id: t.id,
        name: t.title,
        agentId: t.agentId,
        organizationId: "",
        createdById: "",
        archived: false,
        createdAt: t.createdAt,
        updatedAt: t.updatedAt,
      }));
    } catch (error) {
      console.error(`[MicrosoftAgentRunner] Error syncing threads:`, error);
      return [];
    }
  }
  /**
   * Override run() to persist thread when agent run starts
   * 
   * Flow:
   * 1. Create/update thread in Microsoft Agent Framework (Postgres)
   * 2. Call super.run() to handle streaming
   * 3. Update thread on completion
   */
  override run(request: AgentRunnerRunRequest) {
    const { threadId, agent } = request;
    const agentId = agent.name || "minerU";
    
    // Persist thread creation to Microsoft Agent Framework
    this.createOrUpdateThread(threadId, agentId, "Conversation")
      .catch(err => {
        console.error(`[MicrosoftAgentRunner] Failed to persist thread ${threadId}:`, err);
      });

    // Call parent run() and add persistence hooks + state handling
    return super.run(request).pipe(
      // Parse agent state from DataContent
      map((event) => {
        // Check if event contains DataContent with agent state
        if (event.type === "agent_message_chunk" && event.content) {
          const chunks = Array.isArray(event.content) ? event.content : [event.content];
          
          for (const chunk of chunks) {
            // Look for application/json DataContent (agent state)
            if (
              chunk &&
              typeof chunk === "object" &&
              "type" in chunk &&
              chunk.type === "data" &&
              "mimeType" in chunk &&
              chunk.mimeType === "application/json" &&
              "data" in chunk
            ) {
              try {
                // Parse state from base64 or direct string
                let stateJson: string;
                if (typeof chunk.data === "string") {
                  // Could be base64 or direct JSON
                  try {
                    stateJson = Buffer.from(chunk.data, "base64").toString("utf-8");
                  } catch {
                    stateJson = chunk.data;
                  }
                } else {
                  stateJson = JSON.stringify(chunk.data);
                }
                
                const state = JSON.parse(stateJson);
                console.log("[MicrosoftAgentRunner] Parsed agent state:", state);
                
                // Transform to CopilotKit state update event
                return {
                  ...event,
                  type: "agent_state_update" as any,
                  state,
                };
              } catch (err) {
                console.error("[MicrosoftAgentRunner] Failed to parse agent state:", err);
              }
            }
          }
        }
        
        return event;
      }),
      // Optional: Log events as they stream
      tap((event) => {
        if (event.type === "agent_message_start") {
          // Update thread timestamp on new messages
          this.updateThreadTimestamp(threadId).catch(console.error);
        }
      }),
      // Update thread when run completes
      finalize(() => {
        this.updateThreadTimestamp(threadId).catch(console.error);
      })
    );
  }

  /**
   * Override connect() to handle reconnection
   * 
   * This is called when:
   * - Page refresh/reload
   * - User switches back to an existing thread
   * - Hydrating conversation history
   * 
   * Important: connect() may be called BEFORE any run() for a thread
   * (e.g., page load with threadId in URL)
   */
  override connect(request: AgentRunnerConnectRequest) {
    const { threadId } = request;
    
    // Verify thread exists in Microsoft Agent Framework
    // If not found, create a placeholder thread
    this.ensureThreadExists(threadId)
      .catch(err => {
        console.warn(`[MicrosoftAgentRunner] Thread ${threadId} not found, creating placeholder:`, err);
      });

    // Call parent connect() to handle streaming
    return super.connect(request);
  }

  /**
   * Create or update thread in Microsoft Agent Framework
   */
  private async createOrUpdateThread(
    threadId: string,
    agentId: string,
    title: string
  ): Promise<void> {
    try {
      // Try to update existing thread first
      const updateResponse = await fetch(`${AGENT_URL}/api/threads/${threadId}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ title }),
      });

      if (updateResponse.ok) {
        console.log(`[MicrosoftAgentRunner] Updated thread ${threadId}`);
        return;
      }

      // If thread doesn't exist (404), create it
      if (updateResponse.status === 404) {
        const createResponse = await fetch(`${AGENT_URL}/api/threads`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            threadId, // Pass threadId from CopilotKit to use as custom ID
            agentId,
            title: `${title} - ${new Date().toLocaleString()}`,
          }),
        });

        if (createResponse.ok) {
          const thread = await createResponse.json();
          console.log(`[MicrosoftAgentRunner] Created thread ${thread.id}`);
        } else {
          console.error(`[MicrosoftAgentRunner] Failed to create thread: ${createResponse.status}`);
        }
      }
    } catch (error) {
      console.error(`[MicrosoftAgentRunner] Error persisting thread:`, error);
      throw error;
    }
  }

  /**
   * Update thread timestamp (bumps updatedAt)
   */
  private async updateThreadTimestamp(threadId: string): Promise<void> {
    try {
      const response = await fetch(`${AGENT_URL}/api/threads/${threadId}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({}), // Empty update just to bump timestamp
      });

      if (!response.ok && response.status !== 404) {
        console.error(`[MicrosoftAgentRunner] Failed to update thread timestamp: ${response.status}`);
      }
    } catch (error) {
      // Don't throw - this is best-effort
      console.error(`[MicrosoftAgentRunner] Error updating thread timestamp:`, error);
    }
  }

  /**
   * Ensure thread exists in Microsoft Agent Framework
   * Creates a placeholder if not found
   */
  private async ensureThreadExists(threadId: string): Promise<void> {
    try {
      // Check if thread exists
      const response = await fetch(`${AGENT_URL}/api/threads/${threadId}`);
      
      if (response.ok) {
        console.log(`[MicrosoftAgentRunner] Thread ${threadId} exists`);
        return;
      }

      // Thread not found - this is OK on first connect
      // The thread will be created when user sends first message (run() is called)
      console.log(`[MicrosoftAgentRunner] Thread ${threadId} not found yet (will be created on first message)`);
    } catch (error) {
      console.error(`[MicrosoftAgentRunner] Error checking thread existence:`, error);
    }
  }
}
