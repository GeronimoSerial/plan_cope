"use client";

import { useId, useRef, type KeyboardEvent } from "react";

export interface TabItem {
  id: string;
  label: string;
}

interface TabsProps {
  tabs: TabItem[];
  activeId: string;
  onChange: (id: string) => void;
  ariaLabel: string;
}

// Tablist accesible: roving focus + flechas izquierda/derecha + Home/End.
export function Tabs({ tabs, activeId, onChange, ariaLabel }: TabsProps) {
  const baseId = useId();
  const refs = useRef<Array<HTMLButtonElement | null>>([]);

  function focusTab(index: number) {
    const clamped = (index + tabs.length) % tabs.length;
    refs.current[clamped]?.focus();
    onChange(tabs[clamped].id);
  }

  function onKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    switch (event.key) {
      case "ArrowRight":
      case "ArrowDown":
        event.preventDefault();
        focusTab(index + 1);
        break;
      case "ArrowLeft":
      case "ArrowUp":
        event.preventDefault();
        focusTab(index - 1);
        break;
      case "Home":
        event.preventDefault();
        focusTab(0);
        break;
      case "End":
        event.preventDefault();
        focusTab(tabs.length - 1);
        break;
    }
  }

  return (
    <div className="tabs__list" role="tablist" aria-label={ariaLabel}>
      {tabs.map((tab, index) => {
        const selected = tab.id === activeId;
        return (
          <button
            key={tab.id}
            ref={element => {
              refs.current[index] = element;
            }}
            type="button"
            role="tab"
            id={`${baseId}-tab-${tab.id}`}
            aria-selected={selected}
            aria-controls={`${baseId}-panel-${tab.id}`}
            tabIndex={selected ? 0 : -1}
            className="tabs__tab"
            onClick={() => onChange(tab.id)}
            onKeyDown={event => onKeyDown(event, index)}
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}

interface TabPanelProps {
  id: string;
  active: boolean;
  children: React.ReactNode;
}

export function TabPanel({ id, active, children }: TabPanelProps) {
  return (
    <div role="tabpanel" aria-labelledby={`tab-${id}`} hidden={!active}>
      {active && children}
    </div>
  );
}
