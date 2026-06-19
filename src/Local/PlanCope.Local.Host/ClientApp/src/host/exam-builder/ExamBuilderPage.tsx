import { zodResolver } from "@hookform/resolvers/zod";
import { useMemo, useRef, useState } from "react";
import { type Resolver, useForm } from "react-hook-form";
import { ApiClient } from "../api/apiClient";
import { localExamJsonSchema } from "./examSchema";
import {
  buildExamJson,
  createDefaultBlock,
  createEmptyExam,
  createStableId,
  imageFileToAsset,
  normalizeExam,
  serializeExamJson
} from "./examBuilderUtils";
import type {
  ExamBlock,
  ExamBlockType,
  ExamBuilderFormValues,
  ExamOption,
  LocalExamJson
} from "./examTypes";

type ExamBuilderPageProps = {
  apiBaseUrl: string;
};

const blockTypeLabels: Record<ExamBlockType, string> = {
  text: "Texto",
  image: "Imagen",
  multiple_choice: "Opcion multiple",
  true_false: "Verdadero/falso",
  short_answer: "Respuesta corta"
};

export function ExamBuilderPage({ apiBaseUrl }: ExamBuilderPageProps) {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [selectedBlockType, setSelectedBlockType] = useState<ExamBlockType>("multiple_choice");
  const [validationMessage, setValidationMessage] = useState("");
  const [apiMessage, setApiMessage] = useState("");
  const apiClient = useMemo(() => new ApiClient(apiBaseUrl), [apiBaseUrl]);

  const {
    register,
    reset,
    setValue,
    watch,
    formState: { errors }
  } = useForm<ExamBuilderFormValues>({
    defaultValues: createEmptyExam(),
    resolver: zodResolver(localExamJsonSchema) as Resolver<ExamBuilderFormValues>,
    mode: "onChange"
  });

  const values = watch();
  const jsonPreview = useMemo(() => {
    try {
      return serializeExamJson(buildExamJson(values));
    } catch {
      return serializeExamJson(values as LocalExamJson);
    }
  }, [values]);

  const updateBlocks = (blocks: ExamBlock[]) => setValue("blocks", blocks, { shouldDirty: true, shouldValidate: true });
  const updateAssets = (assets: ExamBuilderFormValues["assets"]) =>
    setValue("assets", assets, { shouldDirty: true, shouldValidate: true });

  function addBlock() {
    updateBlocks([...values.blocks, createDefaultBlock(selectedBlockType)]);
  }

  function updateBlock(index: number, block: ExamBlock) {
    updateBlocks(values.blocks.map((current, currentIndex) => (currentIndex === index ? block : current)));
  }

  function duplicateBlock(index: number) {
    const source = values.blocks[index];
    const clone = { ...structuredClone(source), id: createStableId(source.type.replace("_", "-")) };
    updateBlocks([...values.blocks.slice(0, index + 1), clone, ...values.blocks.slice(index + 1)]);
  }

  function removeBlock(index: number) {
    updateBlocks(values.blocks.filter((_, currentIndex) => currentIndex !== index));
  }

  function moveBlock(index: number, direction: -1 | 1) {
    const target = index + direction;
    if (target < 0 || target >= values.blocks.length) {
      return;
    }

    const next = [...values.blocks];
    [next[index], next[target]] = [next[target], next[index]];
    updateBlocks(next);
  }

  async function addAsset(file: File | undefined) {
    if (!file) {
      return;
    }

    try {
      updateAssets([...values.assets, await imageFileToAsset(file)]);
      setValidationMessage("");
    } catch (error) {
      setValidationMessage(error instanceof Error ? error.message : "No se pudo cargar la imagen.");
    }
  }

  function removeAsset(assetId: string) {
    updateAssets(values.assets.filter(asset => asset.id !== assetId));
  }

  function validateCurrentExam(): LocalExamJson | null {
    try {
      const examJson = buildExamJson(values);
      setValidationMessage("JSON valido para importar.");
      return examJson;
    } catch (error) {
      setValidationMessage(error instanceof Error ? error.message : "El examen no es valido.");
      return null;
    }
  }

  function exportJson() {
    const examJson = validateCurrentExam();
    if (!examJson) {
      return;
    }

    const blob = new Blob([serializeExamJson(examJson)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `${examJson.examCode || "examen"}.json`;
    link.click();
    URL.revokeObjectURL(url);
  }

  async function importJson(file: File | undefined) {
    if (!file) {
      return;
    }

    try {
      const content = await file.text();
      reset(normalizeExam(JSON.parse(content)));
      setValidationMessage(`Importado ${file.name}.`);
      setApiMessage("");
    } catch (error) {
      setValidationMessage(error instanceof Error ? error.message : "No se pudo importar el JSON.");
    } finally {
      if (fileInputRef.current) {
        fileInputRef.current.value = "";
      }
    }
  }

  async function importIntoLocalApi() {
    const examJson = validateCurrentExam();
    if (!examJson) {
      return;
    }

    try {
      setApiMessage("Importando en la API local...");
      await apiClient.importExam(examJson);
      setApiMessage(`Importado en API local: ${examJson.examCode} v${examJson.versionNumber ?? 1}.`);
    } catch (error) {
      setApiMessage(error instanceof Error ? error.message : "La API local rechazo el examen.");
    }
  }

  return (
    <section className="builder-page">
      <div className="builder-toolbar">
        <div>
          <p className="eyebrow">Autoria JSON-first</p>
          <h2>Creador de examenes</h2>
        </div>
        <div className="builder-actions">
          <input
            ref={fileInputRef}
            className="visually-hidden"
            type="file"
            accept="application/json,.json"
            onChange={event => void importJson(event.target.files?.[0])}
          />
          <button className="button button-secondary" type="button" onClick={() => fileInputRef.current?.click()}>
            Importar JSON
          </button>
          <button className="button button-secondary" type="button" onClick={validateCurrentExam}>
            Validar
          </button>
          <button className="button button-secondary" type="button" onClick={importIntoLocalApi}>
            Importar en API local
          </button>
          <button className="button button-primary" type="button" onClick={exportJson}>
            Exportar JSON
          </button>
        </div>
      </div>

      {(validationMessage || apiMessage) && (
        <div className="builder-status">
          {validationMessage && <p>{validationMessage}</p>}
          {apiMessage && <p>{apiMessage}</p>}
        </div>
      )}

      <div className="builder-layout">
        <div className="builder-main">
          <section className="panel">
            <div className="section-title">
              <h2>Metadata</h2>
              <p>Estos campos se exportan directo al formato local.</p>
            </div>
            <div className="form-grid">
              <label className="field">
                ID
                <input className="control" {...register("id")} />
              </label>
              <label className="field">
                Codigo de examen
                <input className="control" {...register("examCode")} />
                {errors.examCode && <span className="field-error">{errors.examCode.message}</span>}
              </label>
              <label className="field">
                Titulo
                <input className="control" {...register("title")} />
                {errors.title && <span className="field-error">{errors.title.message}</span>}
              </label>
              <label className="field">
                Version
                <input className="control" type="number" min="1" {...register("versionNumber", { valueAsNumber: true })} />
              </label>
              <label className="field">
                Grado
                <input className="control" {...register("grade")} />
              </label>
              <label className="field">
                Division
                <input className="control" {...register("division")} />
              </label>
              <label className="field builder-wide">
                Materia
                <input className="control" {...register("subject")} />
              </label>
            </div>
          </section>

          <section className="panel">
            <div className="section-title">
              <h2>Assets</h2>
              <p>Las imagenes quedan embebidas como base64.</p>
            </div>
            <label className="button button-secondary builder-file-button">
              Cargar imagen
              <input type="file" accept="image/*" onChange={event => void addAsset(event.target.files?.[0])} />
            </label>
            <div className="asset-list">
              {values.assets.length === 0 && <p className="empty-state">Todavia no hay assets cargados.</p>}
              {values.assets.map(asset => (
                <div className="asset-row" key={asset.id}>
                  <img src={`data:${asset.mimeType};base64,${asset.contentBase64}`} alt={asset.fileName} />
                  <div>
                    <strong>{asset.fileName}</strong>
                    <span>{asset.id}</span>
                  </div>
                  <button className="button button-secondary" type="button" onClick={() => removeAsset(asset.id)}>
                    Quitar
                  </button>
                </div>
              ))}
            </div>
          </section>

          <section className="panel">
            <div className="builder-section-header">
              <div className="section-title">
                <h2>Bloques</h2>
                <p>Alta, edicion, duplicado, eliminacion y orden manual inicial.</p>
              </div>
              <div className="builder-add-block">
                <select
                  className="control"
                  value={selectedBlockType}
                  onChange={event => setSelectedBlockType(event.target.value as ExamBlockType)}
                >
                  {Object.entries(blockTypeLabels).map(([type, label]) => (
                    <option key={type} value={type}>
                      {label}
                    </option>
                  ))}
                </select>
                <button className="button button-primary" type="button" onClick={addBlock}>
                  Agregar
                </button>
              </div>
            </div>

            <div className="block-list">
              {values.blocks.length === 0 && <p className="empty-state">Agrega el primer bloque del examen.</p>}
              {values.blocks.map((block, index) => (
                <BlockEditor
                  assets={values.assets}
                  block={block}
                  index={index}
                  isFirst={index === 0}
                  isLast={index === values.blocks.length - 1}
                  key={block.id}
                  onChange={next => updateBlock(index, next)}
                  onDuplicate={() => duplicateBlock(index)}
                  onMoveDown={() => moveBlock(index, 1)}
                  onMoveUp={() => moveBlock(index, -1)}
                  onRemove={() => removeBlock(index)}
                />
              ))}
            </div>
          </section>
        </div>

        <aside className="builder-side">
          <section className="panel">
            <div className="section-title">
              <h2>JSON</h2>
              <p>Vista de salida estable del builder.</p>
            </div>
            <pre className="json-preview">{jsonPreview}</pre>
          </section>
        </aside>
      </div>
    </section>
  );
}

type BlockEditorProps = {
  assets: ExamBuilderFormValues["assets"];
  block: ExamBlock;
  index: number;
  isFirst: boolean;
  isLast: boolean;
  onChange: (block: ExamBlock) => void;
  onDuplicate: () => void;
  onMoveDown: () => void;
  onMoveUp: () => void;
  onRemove: () => void;
};

function BlockEditor({
  assets,
  block,
  index,
  isFirst,
  isLast,
  onChange,
  onDuplicate,
  onMoveDown,
  onMoveUp,
  onRemove
}: BlockEditorProps) {
  return (
    <article className="block-card">
      <header className="block-card-header">
        <div>
          <span>#{index + 1}</span>
          <strong>{blockTypeLabels[block.type]}</strong>
        </div>
        <div className="block-actions">
          <button className="mini-button" type="button" disabled={isFirst} onClick={onMoveUp}>
            Subir
          </button>
          <button className="mini-button" type="button" disabled={isLast} onClick={onMoveDown}>
            Bajar
          </button>
          <button className="mini-button" type="button" onClick={onDuplicate}>
            Duplicar
          </button>
          <button className="mini-button mini-button-danger" type="button" onClick={onRemove}>
            Eliminar
          </button>
        </div>
      </header>

      <label className="field">
        ID del bloque
        <input className="control" value={block.id} onChange={event => onChange({ ...block, id: event.target.value })} />
      </label>

      {block.type === "text" && (
        <label className="field">
          Contenido
          <textarea
            className="control textarea-control"
            value={block.config.content}
            onChange={event => onChange({ ...block, config: { content: event.target.value } })}
          />
        </label>
      )}

      {block.type === "image" && (
        <div className="form-grid">
          <label className="field builder-wide">
            Asset
            <select
              className="control"
              value={block.config.assetId}
              onChange={event => onChange({ ...block, config: { ...block.config, assetId: event.target.value } })}
            >
              <option value="">Seleccionar asset</option>
              {assets.map(asset => (
                <option key={asset.id} value={asset.id}>
                  {asset.fileName} ({asset.id})
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            Texto alternativo
            <input
              className="control"
              value={block.config.alt ?? ""}
              onChange={event => onChange({ ...block, config: { ...block.config, alt: event.target.value } })}
            />
          </label>
          <label className="field">
            Epigrafe
            <input
              className="control"
              value={block.config.caption ?? ""}
              onChange={event => onChange({ ...block, config: { ...block.config, caption: event.target.value } })}
            />
          </label>
        </div>
      )}

      {block.type === "multiple_choice" && (
        <MultipleChoiceEditor block={block} onChange={onChange} />
      )}

      {block.type === "true_false" && (
        <QuestionEditor
          answerOptions={[
            { label: "Verdadero", value: "true" },
            { label: "Falso", value: "false" }
          ]}
          question={block.config.question}
          answerKey={typeof block.answerKey?.correctAnswer === "boolean" ? String(block.answerKey.correctAnswer) : ""}
          required={block.validation?.required ?? false}
          scoreValue={block.answerKey?.scoreValue ?? 1}
          onAnswerKeyChange={value =>
            onChange({
              ...block,
              answerKey: value === "" ? undefined : { correctAnswer: value === "true", scoreValue: block.answerKey?.scoreValue ?? 1 }
            })
          }
          onQuestionChange={question => onChange({ ...block, config: { question } })}
          onRequiredChange={required => onChange({ ...block, validation: { required } })}
          onScoreChange={scoreValue =>
            onChange({
              ...block,
              answerKey: block.answerKey ? { ...block.answerKey, scoreValue } : { correctAnswer: true, scoreValue }
            })
          }
        />
      )}

      {block.type === "short_answer" && (
        <QuestionEditor
          answerOptions={[]}
          question={block.config.prompt}
          answerKey={typeof block.answerKey?.correctAnswer === "string" ? block.answerKey.correctAnswer : ""}
          required={block.validation?.required ?? false}
          scoreValue={block.answerKey?.scoreValue ?? 1}
          onAnswerKeyChange={value =>
            onChange({
              ...block,
              answerKey: value.trim() === "" ? undefined : { correctAnswer: value, scoreValue: block.answerKey?.scoreValue ?? 1 }
            })
          }
          onQuestionChange={prompt => onChange({ ...block, config: { prompt } })}
          onRequiredChange={required => onChange({ ...block, validation: { required } })}
          onScoreChange={scoreValue =>
            onChange({
              ...block,
              answerKey: block.answerKey ? { ...block.answerKey, scoreValue } : { correctAnswer: "", scoreValue }
            })
          }
        />
      )}
    </article>
  );
}

type MultipleChoiceBlock = Extract<ExamBlock, { type: "multiple_choice" }>;

function MultipleChoiceEditor({ block, onChange }: { block: MultipleChoiceBlock; onChange: (block: ExamBlock) => void }) {
  function updateOptions(options: ExamOption[]) {
    onChange({ ...block, config: { ...block.config, options } });
  }

  return (
    <>
      <QuestionEditor
        answerOptions={block.config.options}
        question={block.config.question}
        answerKey={typeof block.answerKey?.correctAnswer === "string" ? block.answerKey.correctAnswer : ""}
        required={block.validation?.required ?? false}
        scoreValue={block.answerKey?.scoreValue ?? 1}
        onAnswerKeyChange={value =>
          onChange({
            ...block,
            answerKey: value === "" ? undefined : { correctAnswer: value, scoreValue: block.answerKey?.scoreValue ?? 1 }
          })
        }
        onQuestionChange={question => onChange({ ...block, config: { ...block.config, question } })}
        onRequiredChange={required => onChange({ ...block, validation: { required } })}
        onScoreChange={scoreValue =>
          onChange({
            ...block,
            answerKey: block.answerKey
              ? { ...block.answerKey, scoreValue }
              : { correctAnswer: block.config.options[0]?.value ?? "", scoreValue }
          })
        }
      />

      <div className="option-list">
        <strong>Opciones</strong>
        {block.config.options.map((option, optionIndex) => (
          <div className="option-row" key={optionIndex}>
            <input
              className="control"
              value={option.value}
              aria-label="Valor"
              onChange={event =>
                updateOptions(
                  block.config.options.map((current, currentIndex) =>
                    currentIndex === optionIndex ? { ...current, value: event.target.value } : current
                  )
                )
              }
            />
            <input
              className="control"
              value={option.label}
              aria-label="Etiqueta"
              onChange={event =>
                updateOptions(
                  block.config.options.map((current, currentIndex) =>
                    currentIndex === optionIndex ? { ...current, label: event.target.value } : current
                  )
                )
              }
            />
            <button
              className="mini-button mini-button-danger"
              type="button"
              onClick={() => updateOptions(block.config.options.filter((_, currentIndex) => currentIndex !== optionIndex))}
            >
              Quitar
            </button>
          </div>
        ))}
        <button
          className="button button-secondary"
          type="button"
          onClick={() =>
            updateOptions([
              ...block.config.options,
              { value: `opcion-${block.config.options.length + 1}`, label: `Opcion ${block.config.options.length + 1}` }
            ])
          }
        >
          Agregar opcion
        </button>
      </div>
    </>
  );
}

type QuestionEditorProps = {
  answerOptions: ExamOption[];
  question: string;
  answerKey: string;
  required: boolean;
  scoreValue: number;
  onAnswerKeyChange: (value: string) => void;
  onQuestionChange: (value: string) => void;
  onRequiredChange: (value: boolean) => void;
  onScoreChange: (value: number) => void;
};

function QuestionEditor({
  answerOptions,
  question,
  answerKey,
  required,
  scoreValue,
  onAnswerKeyChange,
  onQuestionChange,
  onRequiredChange,
  onScoreChange
}: QuestionEditorProps) {
  return (
    <>
      <label className="field">
        Consigna
        <textarea className="control textarea-control" value={question} onChange={event => onQuestionChange(event.target.value)} />
      </label>
      <div className="answer-settings">
        <label>
          <input type="checkbox" checked={required} onChange={event => onRequiredChange(event.target.checked)} />
          Obligatoria
        </label>
        <label className="field">
          Puntaje
          <input
            className="control"
            min="0"
            type="number"
            value={scoreValue}
            onChange={event => onScoreChange(Number(event.target.value))}
          />
        </label>
        {answerOptions.length > 0 ? (
          <label className="field">
            Respuesta correcta
            <select className="control" value={answerKey} onChange={event => onAnswerKeyChange(event.target.value)}>
              <option value="">Sin clave</option>
              {answerOptions.map(option => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
        ) : (
          <label className="field">
            Respuesta esperada
            <input className="control" value={answerKey} onChange={event => onAnswerKeyChange(event.target.value)} />
          </label>
        )}
      </div>
    </>
  );
}
