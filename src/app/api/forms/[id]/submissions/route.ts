import { NextRequest, NextResponse } from "next/server";
import { submissionsService, formsService } from "@/services";

export async function GET(
  _request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;
    const submissions = await submissionsService.list(id);
    return NextResponse.json(submissions);
  } catch (error) {
    console.error("Failed to fetch submissions:", error);
    return NextResponse.json({ error: "Failed to fetch submissions" }, { status: 500 });
  }
}

export async function POST(
  request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;
    const form = await formsService.get(id);
    if (!form) {
      return NextResponse.json({ error: "Form not found" }, { status: 404 });
    }

    const body = await request.json();
    const submission = await submissionsService.create(id, body);
    return NextResponse.json(submission, { status: 201 });
  } catch (error) {
    console.error("Failed to submit form:", error);
    return NextResponse.json({ error: "Failed to submit form" }, { status: 500 });
  }
}
