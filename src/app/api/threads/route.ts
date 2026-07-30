import { NextRequest, NextResponse } from "next/server";
import { prisma } from "@/lib/prisma";

export async function GET(request: NextRequest) {
  try {
    const agentId = request.nextUrl.searchParams.get("agentId") ?? "minerU";
    const threads = await prisma.thread.findMany({
      where: { agentId },
      orderBy: { updatedAt: "desc" },
      select: {
        id: true,
        agentId: true,
        title: true,
        createdAt: true,
        updatedAt: true,
      },
    });
    return NextResponse.json(threads);
  } catch (error) {
    console.error("Failed to fetch threads:", error);
    return NextResponse.json({ error: "Failed to fetch threads" }, { status: 500 });
  }
}

export async function POST(request: NextRequest) {
  try {
    const body = await request.json();
    const agentId = body.agentId ?? "minerU";
    const title = body.title ?? "New Conversation";

    const thread = await prisma.thread.create({
      data: { agentId, title },
      select: {
        id: true,
        agentId: true,
        title: true,
        createdAt: true,
        updatedAt: true,
      },
    });
    return NextResponse.json(thread, { status: 201 });
  } catch (error) {
    console.error("Failed to create thread:", error);
    return NextResponse.json({ error: "Failed to create thread" }, { status: 500 });
  }
}
