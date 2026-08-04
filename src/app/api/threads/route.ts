import { NextRequest, NextResponse } from "next/server";
import { threadsService } from "@/services";

export async function GET(request: NextRequest) {
  try {
    const agentId = request.nextUrl.searchParams.get("agentId") ?? "minerU";
    const threads = await threadsService.list(agentId);
    return NextResponse.json(threads);
  } catch (error) {
    console.error("Failed to fetch threads:", error);
    return NextResponse.json({ error: "Failed to fetch threads" }, { status: 500 });
  }
}

export async function POST(request: NextRequest) {
  try {
    const body = await request.json();
    const thread = await threadsService.create({
      agentId: body.agentId ?? "minerU",
      title: body.title ?? "New Conversation",
    });
    return NextResponse.json(thread, { status: 201 });
  } catch (error) {
    console.error("Failed to create thread:", error);
    return NextResponse.json({ error: "Failed to create thread" }, { status: 500 });
  }
}
