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

export async function GET() {
  try {
    const forms = await formsService.list();
    const parsed = forms.map((f) => ({
      ...f,
      fields: parseFields(f.fields),
    }));
    return NextResponse.json(parsed);
  } catch (error) {
    console.error("Failed to fetch forms:", error);
    return NextResponse.json({ error: "Failed to fetch forms" }, { status: 500 });
  }
}

export async function POST(request: NextRequest) {
  try {
    const body = (await request.json()) as FormConfig;
    if (!body.title || !body.fields?.length) {
      return NextResponse.json(
        { error: "Title and at least one field are required" },
        { status: 400 }
      );
    }

    const form = await formsService.create({
      title: body.title,
      description: body.description ?? undefined,
      fields: JSON.stringify(body.fields),
    });

    return NextResponse.json(form, { status: 201 });
  } catch (error) {
    console.error("Failed to create form:", error);
    return NextResponse.json({ error: "Failed to create form" }, { status: 500 });
  }
}
