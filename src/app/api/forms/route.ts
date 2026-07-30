import { NextRequest, NextResponse } from "next/server";
import { formsApi } from "@/lib/api-client";
import type { FormConfig } from "@/lib/types";
import { generateEmbedding } from "@/lib/embedding";

export async function GET(request: NextRequest) {
  try {
    const q = request.nextUrl.searchParams.get("q");

    if (q?.trim()) {
      const embedding = await generateEmbedding(q);
      if (!embedding) {
        return NextResponse.json({ error: "Failed to generate embedding for search" }, { status: 500 });
      }
      const embeddingStr = `[${embedding.join(",")}]`;
      const forms = await formsApi.list(embeddingStr);
      return NextResponse.json(forms);
    }

    const forms = await formsApi.list();
    return NextResponse.json(forms);
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

    const embedding = body.description
      ? await generateEmbedding(body.description)
      : null;

    const form = await formsApi.create({
      title: body.title,
      description: body.description ?? undefined,
      fields: JSON.stringify(body.fields),
      embedding: embedding ? `[${embedding.join(",")}]` : undefined,
    });

    return NextResponse.json({ ...form, embedding }, { status: 201 });
  } catch (error) {
    console.error("Failed to create form:", error);
    return NextResponse.json({ error: "Failed to create form" }, { status: 500 });
  }
}
