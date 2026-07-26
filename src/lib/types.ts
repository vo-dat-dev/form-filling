export type AgentState = {
  proverbs: string[];
};

export type FieldType =
  | "text"
  | "number"
  | "email"
  | "tel"
  | "textarea"
  | "select"
  | "checkbox"
  | "radio"
  | "date"
  | "file"
  | "list";

export interface FieldOption {
  label: string;
  value: string;
}

export interface FieldValidation {
  required?: boolean;
  min?: number;
  max?: number;
  minLength?: number;
  maxLength?: number;
  pattern?: string;
}

export interface FormField {
  id: string;
  type: FieldType;
  label: string;
  placeholder?: string;
  helpText?: string;
  required: boolean;
  options?: FieldOption[];
  validation?: FieldValidation;
  subFields?: FormField[];
  order: number;
}

export interface FormConfig {
  title: string;
  description?: string;
  fields: FormField[];
}
