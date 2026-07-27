import { NextRequest, NextResponse } from "next/server";
import { prisma } from "@/lib/prisma";
import type { FormConfig } from "@/lib/types";
import { generateEmbedding } from "@/lib/embedding";

export async function GET(
  _request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;

    const rows = await prisma.$queryRawUnsafe<
      Array<Record<string, unknown>>
    >(
       `SELECT f.*, f.embedding::text AS embedding
        FROM "Form" f WHERE f.id = $1`,
       id,
    );

    if (!rows[0]) {
      return NextResponse.json({ error: "Form not found" }, { status: 404 });
    }

    const { embedding: embStr, ...rest } = rows[0];
    const form = {
      ...rest,
      embedding: embStr ? JSON.parse(embStr as string) : null,
    };

    return NextResponse.json(form);
  } catch (error) {
    console.error("Failed to fetch form:", error);
    return NextResponse.json(
      { error: "Failed to fetch form" },
      { status: 500 }
    );
  }
}

export async function PUT(
  request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;
    const body = (await request.json()) as FormConfig;

    const existing = await prisma.form.findUnique({ where: { id } });
    if (!existing) {
      return NextResponse.json({ error: "Form not found" }, { status: 404 });
    }

    const embedding =
      body.description !== existing.description
        ? await generateEmbedding(body.description ?? "")
        : undefined;

    const form = await prisma.form.update({
      where: { id },
      data: {
        title: body.title,
        description: body.description ?? null,
        fields: JSON.parse(JSON.stringify(body.fields)),
      },
    });

    if (embedding !== undefined) {
      if (embedding) {
        await prisma.$executeRawUnsafe(
          `UPDATE "Form" SET embedding = $1::vector WHERE id = $2`,
          `[${embedding.join(",")}]`,
          form.id,
        );
      } else {
        await prisma.$executeRawUnsafe(
          `UPDATE "Form" SET embedding = NULL WHERE id = $1`,
          form.id,
        );
      }
    }

    const resultEmbedding =
      embedding !== undefined
        ? embedding
        : await (async () => {
            const r = await prisma.$queryRawUnsafe<Array<{ embedding: string | null }>>(
              `SELECT embedding::text AS embedding FROM "Form" WHERE id = $1`,
              form.id);
            return r[0]?.embedding
              ? JSON.parse(r[0].embedding)
              : null;
          })();

    return NextResponse.json({ ...form, embedding: resultEmbedding });
  } catch (error) {
    console.error("Failed to update form:", error);
    return NextResponse.json(
      { error: "Failed to update form" },
      { status: 500 }
    );
  }
}

export async function DELETE(
  _request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;
    const existing = await prisma.form.findUnique({ where: { id } });
    if (!existing) {
      return NextResponse.json({ error: "Form not found" }, { status: 404 });
    }
    await prisma.form.delete({ where: { id } });
    return NextResponse.json({ success: true });
  } catch (error) {
    console.error("Failed to delete form:", error);
    return NextResponse.json(
      { error: "Failed to delete form" },
      { status: 500 }
    );
  }
}
