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
  multiple_choice: "Opción múltiple",
  true_false: "Verdadero/falso",
  short_answer: "Respuesta corta"
};

type BuilderStatus = {
  tone: "info" | "success" | "error";
  text: string;
};

export function ExamBuilderPage({ apiBaseUrl }: ExamBuilderPageProps) {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [selectedBlockType, setSelectedBlockType] = useState<ExamBlockType>("multiple_choice");
  const [validationStatus, setValidationStatus] = useState<BuilderStatus | null>(null);
  const [apiStatus, setApiStatus] = useState<BuilderStatus | null>(null);
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
      setValidationStatus(null);
    } catch (error) {
      setValidationStatus({
        tone: "error",
        text: error instanceof Error ? error.message : "No se pudo cargar la imagen."
      });
    }
  }

  function removeAsset(assetId: string) {
    updateAssets(values.assets.filter(asset => asset.id !== assetId));
  }

  function validateCurrentExam(): LocalExamJson | null {
    try {
      const examJson = buildExamJson(values);
      setValidationStatus({ tone: "success", text: "La estructura del examen es válida." });
      return examJson;
    } catch (error) {
      setValidationStatus({
        tone: "error",
        text: error instanceof Error ? error.message : "El examen no es válido."
      });
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
      setValidationStatus({ tone: "success", text: `Archivo importado: ${file.name}.` });
      setApiStatus(null);
    } catch (error) {
      setValidationStatus({
        tone: "error",
        text: error instanceof Error ? error.message : "No se pudo importar el archivo."
      });
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

    setValidationStatus(null);
    try {
      setApiStatus({ tone: "info", text: "Guardando en este equipo…" });
      await apiClient.importExam(examJson);
      setApiStatus({
        tone: "success",
        text: `Examen guardado en este equipo: ${examJson.examCode} v${examJson.versionNumber ?? 1}.`
      });
    } catch (error) {
      setApiStatus({
        tone: "error",
        text: error instanceof Error ? error.message : "No se pudo guardar el examen en este equipo."
      });
    }
  }

  return (
    <section className="builder-page">
      <div className="builder-toolbar">
        <div>
          <p className="eyebrow">Constructor de exámenes</p>
          <h2>Nuevo examen</h2>
        </div>
        <div className="builder-actions">
          <button className="button button-primary" type="button" onClick={importIntoLocalApi}>
            Guardar en este equipo
          </button>
        </div>
      </div>

      {validationStatus && (
        <div className={`builder-status builder-status-${validationStatus.tone}`} role={validationStatus.tone === "error" ? "alert" : "status"}>
          <p>{validationStatus.text}</p>
        </div>
      )}
      {apiStatus && (
        <div className={`builder-status builder-status-${apiStatus.tone}`} role={apiStatus.tone === "error" ? "alert" : "status"}>
          <p>{apiStatus.text}</p>
        </div>
      )}

      <div className="builder-layout">
        <div className="builder-main">
          <section className="panel">
            <div className="section-title">
              <h2>Datos del examen</h2>
            </div>
            <div className="form-grid">
              <label className="field">
                Código de examen
                <input className="control" {...register("examCode")} />
                {errors.examCode && <span className="field-error">{errors.examCode.message}</span>}
              </label>
              <label className="field">
                Título
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
            <details className="builder-subdetails">
              <summary>Identificador técnico</summary>
              <label className="field">
                ID
                <input className="control" {...register("id")} />
              </label>
            </details>
          </section>

          <section className="panel">
            <div className="section-title">
              <h2>Imágenes</h2>
            </div>
            <label className="button button-secondary builder-file-button">
              Agregar imagen
              <input type="file" accept="image/*" onChange={event => void addAsset(event.target.files?.[0])} />
            </label>
            <div className="asset-list">
              {values.assets.length === 0 && <p className="empty-state">Todavía no hay imágenes.</p>}
              {values.assets.map(asset => (
                <div className="asset-row" key={asset.id}>
                  <img src={`data:${asset.mimeType};base64,${asset.contentBase64}`} alt={asset.fileName} />
                  <div>
                    <strong>{asset.fileName}</strong>
                    <details className="asset-details">
                      <summary>Ver identificador</summary>
                      <span>{asset.id}</span>
                    </details>
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
                  Agregar bloque
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
      </div>

      <details className="builder-advanced">
        <summary>Herramientas avanzadas</summary>
        <div className="builder-advanced-content">
          <div className="builder-actions builder-advanced-actions">
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
              Validar estructura
            </button>
            <button className="button button-secondary" type="button" onClick={exportJson}>
              Exportar JSON
            </button>
          </div>
          <div>
            <h3>Vista JSON</h3>
            <pre className="json-preview">{jsonPreview}</pre>
          </div>
        </div>
      </details>
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
        <details className="block-actions-details">
          <summary>Acciones</summary>
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
        </details>
      </header>

      <details className="builder-subdetails">
        <summary>Configuración avanzada</summary>
        <label className="field">
          ID del bloque
          <input className="control" value={block.id} onChange={event => onChange({ ...block, id: event.target.value })} />
        </label>
      </details>

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
            Imagen
            <select
              className="control"
              value={block.config.assetId}
              onChange={event => onChange({ ...block, config: { ...block.config, assetId: event.target.value } })}
            >
              <option value="">Seleccionar imagen</option>
              {assets.map(asset => (
                <option key={asset.id} value={asset.id}>
                  {asset.fileName}
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
          Agregar opción
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
