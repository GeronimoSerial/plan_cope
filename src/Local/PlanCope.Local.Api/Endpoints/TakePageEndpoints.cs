using System.Net;

namespace PlanCope.Local.Api.Endpoints;

public static class TakePageEndpoints
{
    public static IEndpointRouteBuilder MapTakePageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", () => Results.Redirect("/examen"));
        endpoints.MapGet("/examen", () => Results.Content(PageHtml, "text/html; charset=utf-8"));
        endpoints.MapGet("/examen/{sessionId}", (string sessionId) =>
            Results.Content(PageHtml.Replace("__SESSION_ID__", WebUtility.HtmlEncode(sessionId), StringComparison.Ordinal), "text/html; charset=utf-8"));
        endpoints.MapGet("/toma/{sessionId}", (string sessionId) => Results.Redirect($"/examen/{WebUtility.UrlEncode(sessionId)}"));
        endpoints.MapGet("/take", () => Results.Redirect("/examen"));
        endpoints.MapGet("/take/{sessionId}", (string sessionId) => Results.Redirect($"/examen/{WebUtility.UrlEncode(sessionId)}"));

        return endpoints;
    }

    private const string PageHtml = """
        <!doctype html>
        <html lang="es">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Plan Cope - Toma local</title>
          <style>
            :root { color-scheme: light; --ink:#17202a; --muted:#5d6d7e; --line:#d7dee8; --accent:#0f766e; --paper:#f7f8f5; --panel:#ffffff; --danger:#b42318; }
            * { box-sizing: border-box; }
            body { margin:0; font-family: Georgia, "Times New Roman", serif; background: linear-gradient(180deg, #eef3ef, var(--paper)); color:var(--ink); }
            main { width:min(920px, calc(100% - 28px)); margin:0 auto; padding:28px 0 44px; }
            header { border-bottom:1px solid var(--line); padding-bottom:18px; margin-bottom:22px; }
            h1 { margin:0 0 8px; font-size:clamp(28px, 5vw, 44px); line-height:1.05; letter-spacing:0; }
            h2 { margin:0 0 16px; font-size:22px; }
            p { color:var(--muted); line-height:1.45; }
            .panel { background:var(--panel); border:1px solid var(--line); border-radius:8px; padding:20px; box-shadow:0 8px 28px rgba(23,32,42,.08); margin-bottom:16px; }
            label { display:block; font-weight:700; margin:12px 0 6px; }
            input, textarea { width:100%; border:1px solid var(--line); border-radius:6px; padding:12px; font:inherit; background:#fff; }
            button { border:0; border-radius:6px; padding:12px 16px; font:700 16px Arial, sans-serif; background:var(--accent); color:white; cursor:pointer; }
            button.secondary { background:#34495e; }
            button:disabled { opacity:.55; cursor:not-allowed; }
            .question { border-top:1px solid var(--line); padding-top:16px; margin-top:16px; }
            .options { display:grid; gap:8px; margin-top:10px; }
            .option { display:flex; gap:10px; align-items:flex-start; padding:10px; border:1px solid var(--line); border-radius:6px; background:#fbfcfb; }
            .option input { width:auto; margin-top:4px; }
            .status { font:700 15px Arial, sans-serif; color:var(--muted); }
            .error { color:var(--danger); }
            .hidden { display:none; }
            .actions { display:flex; gap:10px; flex-wrap:wrap; margin-top:18px; }
            @media (max-width: 620px) { main { width:min(100% - 18px, 920px); padding-top:18px; } .panel { padding:15px; } }
          </style>
        </head>
        <body>
          <main>
            <header>
              <h1>Plan Cope</h1>
              <p>Toma local de examen. Funciona dentro de la red de la escuela.</p>
            </header>

            <section id="start" class="panel">
              <h2>Ingresar al examen</h2>
              <label for="sessionId">Codigo de examen</label>
              <input id="sessionId" autocomplete="off" value="__SESSION_ID__">
              <div class="actions">
                <button id="startButton">Comenzar</button>
              </div>
              <p id="startStatus" class="status"></p>
            </section>

            <section id="exam" class="panel hidden">
              <h2>Responder examen</h2>
              <div id="questions"></div>
              <div class="actions">
                <button id="saveButton" class="secondary">Guardar respuestas</button>
                <button id="submitButton">Enviar examen</button>
              </div>
              <p id="examStatus" class="status"></p>
            </section>

            <section id="done" class="panel hidden">
              <h2>Examen enviado</h2>
              <p>Codigo de confirmacion: <strong id="confirmation"></strong></p>
              <p>Entrega registrada en este equipo. No cierres esta pantalla hasta que el docente lo indique.</p>
            </section>
          </main>

          <script>
            const state = { attemptId: null, blocks: [] };
            const byId = id => document.getElementById(id);

            byId("startButton").addEventListener("click", startAttempt);
            byId("saveButton").addEventListener("click", saveAnswers);
            byId("submitButton").addEventListener("click", submitAttempt);

            async function startAttempt() {
              setStatus("startStatus", "Iniciando...");
              const sessionId = byId("sessionId").value.trim();
              if (!sessionId) {
                setStatus("startStatus", "Completa el codigo de examen.", true);
                return;
              }

              const response = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}/attempts`, {
                method: "POST"
              });

              if (!response.ok) {
                setStatus("startStatus", await errorText(response), true);
                return;
              }

              const payload = await response.json();
              state.attemptId = payload.attempt.id;
              state.blocks = payload.blocks;
              renderBlocks();
              byId("start").classList.add("hidden");
              byId("exam").classList.remove("hidden");
            }

            function renderBlocks() {
              byId("questions").innerHTML = "";
              for (const block of state.blocks) {
                const config = JSON.parse(block.configJson);
                const section = document.createElement("section");
                section.className = "question";
                section.dataset.blockId = block.id;
                section.dataset.kind = block.blockType;

                if (block.blockType === 0 || block.blockType === "Text") {
                  section.innerHTML = `<p>${escapeHtml(config.content ?? "")}</p>`;
                } else if (block.blockType === 2 || block.blockType === "MultipleChoice") {
                  section.innerHTML = `<h3>${escapeHtml(config.question ?? "")}</h3><div class="options"></div>`;
                  const options = section.querySelector(".options");
                  for (const option of config.options ?? []) {
                    options.insertAdjacentHTML("beforeend", `<label class="option"><input type="radio" name="${block.id}" value="${escapeHtml(option.value)}"><span>${escapeHtml(option.label)}</span></label>`);
                  }
                } else if (block.blockType === 3 || block.blockType === "TrueFalse") {
                  section.innerHTML = `<h3>${escapeHtml(config.question ?? "")}</h3><div class="options"><label class="option"><input type="radio" name="${block.id}" value="true"><span>Verdadero</span></label><label class="option"><input type="radio" name="${block.id}" value="false"><span>Falso</span></label></div>`;
                } else {
                  section.innerHTML = `<h3>${escapeHtml(config.prompt ?? "")}</h3><textarea rows="5" aria-label="Respuesta"></textarea>`;
                }

                byId("questions").appendChild(section);
              }
            }

            async function saveAnswers() {
              setStatus("examStatus", "Guardando...");
              const response = await fetch(`/api/attempts/${encodeURIComponent(state.attemptId)}/answers`, {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ answers: collectAnswers() })
              });
              setStatus("examStatus", response.ok ? "Respuestas guardadas." : await errorText(response), !response.ok);
              return response.ok;
            }

            async function submitAttempt() {
              if (!await saveAnswers()) return;
              setStatus("examStatus", "Enviando...");
              const response = await fetch(`/api/attempts/${encodeURIComponent(state.attemptId)}/submit`, { method: "POST" });
              if (!response.ok) {
                setStatus("examStatus", await errorText(response), true);
                return;
              }
              const payload = await response.json();
              byId("confirmation").textContent = payload.confirmationCode;
              byId("exam").classList.add("hidden");
              byId("done").classList.remove("hidden");
            }

            function collectAnswers() {
              return [...document.querySelectorAll(".question[data-block-id]")]
                .filter(section => section.dataset.kind !== "0" && section.dataset.kind !== "Text")
                .map(section => {
                  const radio = section.querySelector("input[type='radio']:checked");
                  const textarea = section.querySelector("textarea");
                  return { blockId: section.dataset.blockId, answer: radio ? radio.value : (textarea ? textarea.value : null) };
                });
            }

            async function errorText(response) {
              try {
                const body = await response.json();
                return body.error ?? "No se pudo completar la operacion.";
              } catch {
                return "No se pudo completar la operacion.";
              }
            }

            function setStatus(id, text, error = false) {
              byId(id).textContent = text;
              byId(id).classList.toggle("error", error);
            }

            function escapeHtml(value) {
              return String(value).replace(/[&<>"']/g, char => ({ "&":"&amp;", "<":"&lt;", ">":"&gt;", '"':"&quot;", "'":"&#039;" }[char]));
            }
          </script>
        </body>
        </html>
        """;
}
