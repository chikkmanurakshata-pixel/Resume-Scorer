import { useEffect, useState } from "react";

const API = import.meta.env.VITE_API_URL ?? "http://localhost:5256";

interface Match {
  requirement: string;
  importance: string;
  matchLevel: string;
  evidence: string;
  gap: string;
}

interface Result {
  overallScore: number;
  semanticScore: number;
  keywordScore: number;
  matches: Match[];
  matchedSkills: string[];
  missingSkills: string[];
  matchedKeywords: string[];
  missingKeywords: string[];
  suggestions: string[];
}

type Filter = "All" | "Strong" | "Partial" | "Missing";
const FILTERS: Filter[] = ["All", "Strong", "Partial", "Missing"];

const tone = (score: number) => (score >= 75 ? "good" : score >= 50 ? "mid" : "bad");
const verdict = (score: number) =>
  score >= 75 ? "Strong match" : score >= 50 ? "Partial match" : "Weak match";
const levelTone = (level: string) =>
  level === "Strong" ? "good" : level === "Partial" ? "mid" : "bad";
const fileSize = (b: number) =>
  b > 1048576 ? `${(b / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(b / 1024))} KB`;

const RADIUS = 54;
const CIRC = 2 * Math.PI * RADIUS;

function Dial({ value, empty = false }: { value: number; empty?: boolean }) {
  const [drawn, setDrawn] = useState(0);

  useEffect(() => {
    const id = requestAnimationFrame(() => setDrawn(value));
    return () => cancelAnimationFrame(id);
  }, [value]);

  return (
    <div
      className={`dial ${empty ? "empty" : tone(value)}`}
      role="img"
      aria-label={empty ? "No score yet" : `Score ${value} out of 100`}
    >
      <svg viewBox="0 0 140 140" width="100%" height="100%">
        <circle className="dial-track" cx="70" cy="70" r={RADIUS} />
        <circle
          className="dial-arc"
          cx="70"
          cy="70"
          r={RADIUS}
          strokeDasharray={CIRC}
          strokeDashoffset={CIRC * (1 - drawn / 100)}
          transform="rotate(-90 70 70)"
        />
      </svg>
      <div className="dial-text">
        <strong>{empty ? "–" : value}</strong>
        <span>out of 100</span>
      </div>
    </div>
  );
}

function Bar({ label, value }: { label: string; value: number }) {
  const [width, setWidth] = useState(0);

  useEffect(() => {
    const id = requestAnimationFrame(() => setWidth(value));
    return () => cancelAnimationFrame(id);
  }, [value]);

  return (
    <div className="bar-row">
      <div className="bar-head">
        <span>{label}</span>
        <b>{value}</b>
      </div>
      <div className="bar-track">
        <div className={`bar-fill ${tone(value)}`} style={{ width: `${width}%` }} />
      </div>
    </div>
  );
}

export default function App() {
  const [file, setFile] = useState<File | null>(null);
  const [jd, setJd] = useState("");
  const [result, setResult] = useState<Result | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [dragging, setDragging] = useState(false);
  const [filter, setFilter] = useState<Filter>("All");

  const pick = (f: File | undefined | null) => {
    if (f) {
      setFile(f);
      setError("");
    }
  };

  const submit = async () => {
    if (!file) {
      setError("Choose your resume first.");
      return;
    }
    if (!jd.trim()) {
      setError("Paste the job description to compare against.");
      return;
    }
    setLoading(true);
    setError("");
    setResult(null);
    setFilter("All");

    const form = new FormData();
    form.append("resume", file);
    form.append("jobDescription", jd);

    try {
      const res = await fetch(`${API}/api/score`, { method: "POST", body: form });
      if (!res.ok) throw new Error(await res.text());
      setResult(await res.json());
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      setError(
        msg.includes("Failed to fetch")
          ? "Cannot reach the server. Check that the API is running."
          : msg
      );
    } finally {
      setLoading(false);
    }
  };

  const count = (f: Filter) =>
    !result
      ? 0
      : f === "All"
      ? result.matches.length
      : result.matches.filter(m => m.matchLevel === f).length;

  const visible = result
    ? result.matches.filter(m => filter === "All" || m.matchLevel === filter)
    : [];

  const keywordTotal = result ? result.matchedKeywords.length + result.missingKeywords.length : 0;

  return (
    <div className="shell">
      <header className="top">
        <h1>Resume Scorer</h1>
        <p>See how closely your resume fits a job description, and what to fix before you apply.</p>
      </header>

      <main className="grid">
        <section className="panel input-panel" aria-label="Your documents">
          <h2>Your documents</h2>

          <label
            className={`drop ${dragging ? "drag" : ""}`}
            onDragOver={e => {
              e.preventDefault();
              setDragging(true);
            }}
            onDragLeave={() => setDragging(false)}
            onDrop={e => {
              e.preventDefault();
              setDragging(false);
              pick(e.dataTransfer.files?.[0]);
            }}
          >
            <input
              className="sr-only"
              type="file"
              accept=".pdf,.docx,.txt"
              onChange={e => pick(e.target.files?.[0])}
            />
            {file ? (
              <>
                <span className="drop-name">{file.name}</span>
                <span className="drop-sub">{fileSize(file.size)}. Click to choose a different file.</span>
              </>
            ) : (
              <>
                <span className="drop-name">Drop your resume here</span>
                <span className="drop-sub">or click to browse. PDF, DOCX or TXT, up to 5 MB.</span>
              </>
            )}
          </label>

          <label className="field-label" htmlFor="jd">
            Job description
          </label>
          <textarea
            id="jd"
            rows={10}
            placeholder="Paste the full job description here"
            value={jd}
            onChange={e => setJd(e.target.value)}
          />
          <div className="hint">{jd.length.toLocaleString()} characters</div>

          <button className="primary" onClick={submit} disabled={loading}>
            {loading && <span className="spinner" aria-hidden="true" />}
            {loading ? "Scoring your resume" : "Score my resume"}
          </button>

          {error && (
            <div className="error" role="alert">
              {error}
            </div>
          )}
        </section>

        <section className="results" aria-live="polite">
          {!result && !loading && (
            <div className="empty-state">
              <Dial value={0} empty />
              <h2>Your score will show up here</h2>
              <p>Add your resume and a job description, then press Score my resume.</p>
            </div>
          )}

          {loading && (
            <div className="empty-state">
              <Dial value={0} empty />
              <h2>Reading your resume</h2>
              <p>Matching each requirement against your experience. This takes 10 to 30 seconds.</p>
            </div>
          )}

          {result && (
            <>
              <div className="summary">
                <Dial value={result.overallScore} />
                <div className="summary-body">
                  <h2 className={`verdict ${tone(result.overallScore)}`}>{verdict(result.overallScore)}</h2>
                  <Bar label="Requirement match" value={result.semanticScore} />
                  <Bar label="Keyword coverage" value={result.keywordScore} />
                </div>
              </div>

              <div className="block">
                <div className="block-head">
                  <h3>Keywords</h3>
                  <span className="muted">
                    {result.matchedKeywords.length} of {keywordTotal} found in your resume
                  </span>
                </div>
                <div className="chips">
                  {result.matchedKeywords.map(k => (
                    <span key={k} className="chip good">
                      <span aria-hidden="true">✓</span> {k}
                    </span>
                  ))}
                  {result.missingKeywords.map(k => (
                    <span key={k} className="chip bad">
                      <span aria-hidden="true">✗</span> {k}
                      <span className="sr-only"> (missing)</span>
                    </span>
                  ))}
                </div>
              </div>

              <div className="block">
                <div className="block-head">
                  <h3>Requirements</h3>
                  <div className="seg" role="group" aria-label="Filter requirements">
                    {FILTERS.map(f => (
                      <button
                        key={f}
                        className={filter === f ? "on" : ""}
                        onClick={() => setFilter(f)}
                        aria-pressed={filter === f}
                      >
                        {f} {count(f)}
                      </button>
                    ))}
                  </div>
                </div>

                <ul className="reqs">
                  {visible.map((m, i) => (
                    <li key={i} className={`req ${levelTone(m.matchLevel)}`}>
                      <div className="req-top">
                        <strong>{m.requirement}</strong>
                        <span className={`badge ${levelTone(m.matchLevel)}`}>{m.matchLevel}</span>
                      </div>
                      <div className="req-meta">{m.importance}</div>
                      {m.evidence && (
                        <p className="req-line">
                          <span className="k">In your resume</span> {m.evidence}
                        </p>
                      )}
                      {m.gap && (
                        <p className="req-line">
                          <span className="k">Gap</span> {m.gap}
                        </p>
                      )}
                    </li>
                  ))}
                  {visible.length === 0 && <li className="muted">No requirements in this group.</li>}
                </ul>
              </div>

              <div className="block">
                <div className="block-head">
                  <h3>How to improve it</h3>
                </div>
                <ul className="tips">
                  {result.suggestions.map((s, i) => (
                    <li key={i}>{s}</li>
                  ))}
                </ul>
              </div>
            </>
          )}
        </section>
      </main>
    </div>
  );
}
