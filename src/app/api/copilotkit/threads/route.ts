import { NextRequest, NextResponse } from "next/server";

/**
 * Proxy endpoint for CopilotKit's useThreads hook
 * Forwards requests to Microsoft Agent Framework's thread API
 * 
 * CopilotKit useThreads expects:
 * GET /api/copilotkit/threads?agentId=X
 * Response: { threads: [...], hasNextPage: boolean }
 */

const AGENT_URL = process.env.AGENT_URL || "http://localhost:8000";

export async function GET(request: NextRequest) {
  try {
    const agentId = request.nextUrl.searchParams.get("agentId") || "minerU";
    
    // Forward to Microsoft Agent Framework
    const response = await fetch(`${AGENT_URL}/api/threads?agentId=${agentId}`);
    
    if (!response.ok) {
      throw new Error(`Agent API returned ${response.status}`);
    }
    
    const threads = await response.json();
    
    // Transform to CopilotKit's expected format
    return NextResponse.json({
      threads: threads.map((t: any) => ({
        id: t.id,
        agentId: t.agentId,
        name: t.title, // CopilotKit uses 'name', Agent Framework uses 'title'
        createdAt: t.createdAt,
        updatedAt: t.updatedAt,
        archived: false,
      })),
      hasNextPage: false, // No pagination support yet
    });
  } catch (error) {
    console.error("Failed to fetch threads from agent:", error);
    return NextResponse.json(
      { error: "Failed to fetch threads" },
      { status: 500 }
    );
  }
}

export async function POST(request: NextRequest) {
  try {
    const body = await request.json();
    
    // Forward to Microsoft Agent Framework
    const response = await fetch(`${AGENT_URL}/api/threads`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        agentId: body.agentId || "minerU",
        title: body.name || "New Conversation",
      }),
    });
    
    if (!response.ok) {
      throw new Error(`Agent API returned ${response.status}`);
    }
    
    const thread = await response.json();
    
    return NextResponse.json({
      id: thread.id,
      agentId: thread.agentId,
      name: thread.title,
      createdAt: thread.createdAt,
      updatedAt: thread.updatedAt,
      archived: false,
    }, { status: 201 });
  } catch (error) {
    console.error("Failed to create thread:", error);
    return NextResponse.json(
      { error: "Failed to create thread" },
      { status: 500 }
    );
  }
}
