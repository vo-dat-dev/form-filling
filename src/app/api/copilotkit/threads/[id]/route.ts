import { NextRequest, NextResponse } from "next/server";

/**
 * Proxy endpoint for CopilotKit's useThreads hook
 * Handles individual thread operations (rename, delete, archive)
 */

const AGENT_URL = process.env.AGENT_URL || "http://localhost:8000";

export async function PATCH(
  request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;
    const body = await request.json();
    
    // Forward to Microsoft Agent Framework
    const response = await fetch(`${AGENT_URL}/api/threads/${id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        title: body.name, // CopilotKit sends 'name', Agent Framework expects 'title'
        metadata: body.metadata,
      }),
    });
    
    if (!response.ok) {
      if (response.status === 404) {
        return NextResponse.json({ error: "Thread not found" }, { status: 404 });
      }
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
    });
  } catch (error) {
    console.error("Failed to update thread:", error);
    return NextResponse.json(
      { error: "Failed to update thread" },
      { status: 500 }
    );
  }
}

export async function DELETE(
  request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;
    
    // Forward to Microsoft Agent Framework
    const response = await fetch(`${AGENT_URL}/api/threads/${id}`, {
      method: "DELETE",
    });
    
    if (!response.ok) {
      if (response.status === 404) {
        return NextResponse.json({ error: "Thread not found" }, { status: 404 });
      }
      throw new Error(`Agent API returned ${response.status}`);
    }
    
    return NextResponse.json({ success: true });
  } catch (error) {
    console.error("Failed to delete thread:", error);
    return NextResponse.json(
      { error: "Failed to delete thread" },
      { status: 500 }
    );
  }
}
