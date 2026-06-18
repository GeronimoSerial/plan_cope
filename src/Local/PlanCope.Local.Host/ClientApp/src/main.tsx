import React from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { StudentExamPage } from "./student/StudentExamPage";
import "./styles.css";

const RootApp = window.location.pathname.startsWith("/examen") ? StudentExamPage : App;

createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <RootApp />
  </React.StrictMode>
);
