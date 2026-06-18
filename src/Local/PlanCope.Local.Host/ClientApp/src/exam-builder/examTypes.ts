export const examBlockTypes = ["text", "image", "multiple_choice", "true_false", "short_answer"] as const;

export type ExamBlockType = (typeof examBlockTypes)[number];

export type ExamAsset = {
  id: string;
  fileName: string;
  mimeType: string;
  contentBase64: string;
};

export type ExamOption = {
  value: string;
  label: string;
};

export type ExamValidation = {
  required?: boolean;
};

export type ExamAnswerKey = {
  correctAnswer: string | boolean;
  scoreValue: number;
};

export type TextExamBlock = {
  id: string;
  type: "text";
  config: {
    content: string;
  };
};

export type ImageExamBlock = {
  id: string;
  type: "image";
  config: {
    assetId: string;
    alt?: string;
    caption?: string;
  };
};

export type MultipleChoiceExamBlock = {
  id: string;
  type: "multiple_choice";
  config: {
    question: string;
    options: ExamOption[];
  };
  validation?: ExamValidation;
  answerKey?: ExamAnswerKey;
};

export type TrueFalseExamBlock = {
  id: string;
  type: "true_false";
  config: {
    question: string;
  };
  validation?: ExamValidation;
  answerKey?: ExamAnswerKey;
};

export type ShortAnswerExamBlock = {
  id: string;
  type: "short_answer";
  config: {
    prompt: string;
  };
  validation?: ExamValidation;
  answerKey?: ExamAnswerKey;
};

export type ExamBlock =
  | TextExamBlock
  | ImageExamBlock
  | MultipleChoiceExamBlock
  | TrueFalseExamBlock
  | ShortAnswerExamBlock;

export type LocalExamJson = {
  id?: string;
  examCode: string;
  title: string;
  grade?: string;
  division?: string;
  subject?: string;
  versionNumber?: number;
  assets: ExamAsset[];
  blocks: ExamBlock[];
};

export type ExamBuilderFormValues = {
  id: string;
  examCode: string;
  title: string;
  grade: string;
  division: string;
  subject: string;
  versionNumber: number;
  assets: ExamAsset[];
  blocks: ExamBlock[];
};
