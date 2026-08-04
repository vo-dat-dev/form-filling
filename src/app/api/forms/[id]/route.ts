import { NextRequest, NextResponse } from "next/server";
import { formsService } from "@/services";
import type { FormConfig, FormField } from "@/lib/types";

function parseFields(raw: unknown): FormField[] {
  if (Array.isArray(raw)) return raw as FormField[];
  if (typeof raw === "string") {
    try {
      return JSON.parse(raw) as FormField[];
    } catch {
      return [];
    }
  }
  return [];
}

export async function GET(
  _request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;
    const form = await formsService.get(id);
    if (!form) {
      return NextResponse.json({ error: "Form not found" }, { status: 404 });
    }
    return NextResponse.json({ ...form, fields: parseFields(form.fields) });
  } catch (error) {
    console.error("Failed to fetch form:", error);
    return NextResponse.json({ error: "Failed to fetch form" }, { status: 500 });
  }
}

export async function PUT(
  request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;
    const body = (await request.json()) as FormConfig;

    const existing = await formsService.get(id);
    if (!existing) {
      return NextResponse.json({ error: "Form not found" }, { status: 404 });
    }

    const form = await formsService.update(id, {
      title: body.title,
      description: body.description ?? undefined,
      fields: JSON.stringify(body.fields),
    });

    return NextResponse.json(form);
  } catch (error) {
    console.error("Failed to update form:", error);
    return NextResponse.json({ error: "Failed to update form" }, { status: 500 });
  }
}

export async function DELETE(
  _request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;
    await formsService.delete(id);
    return NextResponse.json({ success: true });
  } catch (error) {
    console.error("Failed to delete form:", error);
    return NextResponse.json({ error: "Failed to delete form" }, { status: 500 });
  }
}
