import { NextRequest, NextResponse } from "next/server";
import { submissionsApi } from "@/lib/api-client";

export async function GET(
  _request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;
    const submission = await submissionsApi.get(id);
    if (!submission) {
      return NextResponse.json({ error: "Submission not found" }, { status: 404 });
    }
    return NextResponse.json(submission);
  } catch (error) {
    console.error("Failed to fetch submission:", error);
    return NextResponse.json({ error: "Failed to fetch submission" }, { status: 500 });
  }
}
