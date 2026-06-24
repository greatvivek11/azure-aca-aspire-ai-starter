import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import path from "node:path";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const viteBin = path.resolve(scriptDir, "../node_modules/vite/bin/vite.js");

const rawPort = process.env.PORT;
const port = rawPort && /^\d+$/.test(rawPort) ? rawPort : "3000";

const args = [
  viteBin,
  "--host",
  "0.0.0.0",
  "--port",
  port,
  ...process.argv.slice(2),
];

const child = spawn(process.execPath, args, {
  stdio: "inherit",
  env: process.env,
});

child.on("exit", (code, signal) => {
  if (signal) {
    process.kill(process.pid, signal);
    return;
  }

  process.exit(code ?? 0);
});
