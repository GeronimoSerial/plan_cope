import React from "react";
import { createRoot } from "react-dom/client";
import { HostApp } from "./host/HostApp";
import { StudentApp } from "./student/StudentApp";
import "./shared/shared.css";
import "./host/host.css";
import "./student/student.css";

const RootApp = window.location.pathname.startsWith("/examen") ? StudentApp : HostApp;

createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <RootApp />
  </React.StrictMode>
);
