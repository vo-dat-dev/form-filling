import { NextRequest, NextResponse } from "next/server";
import { threadsApi } from "@/lib/api-client";

export async function PATCH(
  request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;
    const body = await request.json();
    const thread = await threadsApi.update(id, {
      title: body.title,
      metadata: body.metadata ?? undefined,
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
    await threadsApi.delete(id);
    return NextResponse.json({ success: true });
  } catch (error) {
    console.error("Failed to delete thread:", error);
    return NextResponse.json({ error: "Failed to delete thread" }, { status: 500 });
  }
}
