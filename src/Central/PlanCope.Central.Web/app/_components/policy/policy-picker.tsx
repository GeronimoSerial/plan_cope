"use client";

import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableHead,
  TableHeader,
  TableRow
} from "@/components/ui/table";
import {
  scoringPolicies,
  scoringPolicyExplanations,
  scoringPolicyLabels,
  scoringPolicyTerms,
  scoringPolicyWarnings,
  type ScoringPolicy
} from "../../_lib/schema/exam";
import { exampleScore, formatScore, referenceExample } from "../../_lib/exams/policy-examples";
import { TermHint } from "../help/term-hint";

interface PolicyPickerProps {
  value: ScoringPolicy | null;
  onChange: (value: ScoringPolicy) => void;
  disabled?: boolean;
  idPrefix?: string;
}

// Controlled scoring-policy picker with a worked example. Reused by the builder and, later,
// by the grading-policies tool.
export function PolicyPicker({ value, onChange, disabled = false, idPrefix = "policy" }: PolicyPickerProps) {
  return (
    <div className="grid gap-4">
      <RadioGroup
        value={value}
        onValueChange={next => onChange(next as ScoringPolicy)}
        disabled={disabled}
        aria-label="Regla de puntaje"
        className="gap-3"
      >
        {scoringPolicies.map(policy => {
          const optionId = `${idPrefix}-${policy}`;
          const selected = value === policy;
          return (
            <div key={policy} className="flex items-start gap-2">
              <RadioGroupItem id={optionId} value={policy} disabled={disabled} className="mt-0.5" />
              <div className="grid gap-0.5">
                <span className="inline-flex items-center gap-1.5">
                  <label htmlFor={optionId} className="text-sm font-medium">
                    {scoringPolicyLabels[policy]}
                  </label>
                  <TermHint term={scoringPolicyTerms[policy]} />
                </span>
                <span className="text-xs text-muted-foreground">{scoringPolicyExplanations[policy]}</span>
                {selected && scoringPolicyWarnings[policy] && (
                  <span className="text-xs text-muted-foreground">{scoringPolicyWarnings[policy]}</span>
                )}
              </div>
            </div>
          );
        })}
      </RadioGroup>

      <Table className="caption-top">
        <TableCaption className="caption-top mt-0 mb-2 text-left text-foreground/80">
          Pregunta con 2 respuestas correctas, vale 1 punto. El alumno marca 1 correcta y 1 incorrecta:
        </TableCaption>
        <TableHeader>
          <TableRow>
            <TableHead>Regla</TableHead>
            <TableHead className="text-right">Puntos obtenidos</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {scoringPolicies.map(policy => (
            <TableRow
              key={policy}
              data-state={value === policy ? "selected" : undefined}
              className="data-[state=selected]:bg-muted/60"
            >
              <TableCell className="font-medium">{scoringPolicyLabels[policy]}</TableCell>
              <TableCell className="text-right tabular-nums">{formatScore(exampleScore(policy, referenceExample))}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
