import { spawnSync } from 'node:child_process';
import {
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const projectRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const committedRoot = join(projectRoot, 'src/app/generated/api');
const temporaryRoot = mkdtempSync(join(tmpdir(), 'unload-generated-api-'));

try {
  const generator = join(projectRoot, 'node_modules/ng-openapi-gen/lib/index.js');
  const result = spawnSync(
    process.execPath,
    [generator, '--config', 'ng-openapi-gen.json', '--output', temporaryRoot, '--silent', 'true'],
    { cwd: projectRoot, stdio: 'inherit' },
  );

  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }

  const expectedFiles = collectFiles(temporaryRoot);
  const committedFiles = collectFiles(committedRoot);
  const differences = new Set([
    ...expectedFiles.filter((path) => !committedFiles.includes(path)),
    ...committedFiles.filter((path) => !expectedFiles.includes(path)),
  ]);

  for (const path of expectedFiles.filter((candidate) => committedFiles.includes(candidate))) {
    const expected = readFileSync(join(temporaryRoot, path));
    const committed = readFileSync(join(committedRoot, path));
    if (!expected.equals(committed)) {
      differences.add(path);
    }
  }

  if (differences.size > 0) {
    console.error('Generated API client is stale:');
    for (const path of [...differences].sort()) {
      console.error(`- ${path}`);
    }
    console.error('Run npm run generate:api and commit the generated files.');
    process.exitCode = 1;
  } else {
    console.log(`Generated API client is current (${committedFiles.length} files).`);
  }
} finally {
  rmSync(temporaryRoot, { recursive: true, force: true });
}

function collectFiles(root) {
  const files = [];
  const visit = (directory) => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const fullPath = join(directory, entry.name);
      if (entry.isDirectory()) {
        visit(fullPath);
      } else {
        files.push(relative(root, fullPath));
      }
    }
  };

  visit(root);
  return files.sort();
}
