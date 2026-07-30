import { NextRequest, NextResponse } from "next/server";
import { prisma } from "@/lib/prisma";

export async function PATCH(
  request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;
    const body = await request.json();

    const thread = await prisma.thread.update({
      where: { id },
      data: {
        title: body.title,
        metadata: body.metadata ?? undefined,
      },
      select: {
        id: true,
        agentId: true,
        title: true,
        createdAt: true,
        updatedAt: true,
      },
    });
    return NextResponse.json(thread);
  } catch (error) {
    console.error("Failed to update thread:", error);
    return NextResponse.json({ error: "Failed to update thread" }, { status: 500 });
  }
}

export async function DELETE(
  request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;
    await prisma.thread.delete({ where: { id } });
    return NextResponse.json({ success: true });
  } catch (error) {
    console.error("Failed to delete thread:", error);
    return NextResponse.json({ error: "Failed to delete thread" }, { status: 500 });
  }
}
