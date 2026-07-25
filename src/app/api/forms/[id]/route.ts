import { NextRequest, NextResponse } from "next/server";
import { prisma } from "@/lib/prisma";
import type { FormConfig } from "@/lib/types";

export async function GET(
  _request: NextRequest,
  { params }: { params: Promise<{ id: string }> }
) {
  try {
    const { id } = await params;
    const form = await prisma.form.findUnique({ where: { id } });
    if (!form) {
      return NextResponse.json({ error: "Form not found" }, { status: 404 });
    }
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

    const form = await prisma.form.update({
      where: { id },
      data: {
        title: body.title,
        description: body.description ?? null,
        fields: JSON.parse(JSON.stringify(body.fields)),
      },
    });
    return NextResponse.json(form);
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
