declare module 'node:child_process' {
  export function spawn(
    command: string,
    args: readonly string[],
    options?: {
      cwd?: string;
      env?: Record<string, string | undefined>;
      stdio?: readonly string[];
    },
  ): {
    stdin: { end(): void };
    stdout: { on(event: 'data', listener: (chunk: string) => void): void };
    stderr: { on(event: 'data', listener: (chunk: string) => void): void };
    on(event: 'exit', listener: (code: number | null) => void): void;
    kill(): boolean;
  };
}

declare module 'node:fs' {
  export function existsSync(path: string): boolean;
}

declare module 'node:path' {
  export function dirname(path: string): string;
  export function join(...paths: string[]): string;
}

declare module 'node:url' {
  export function fileURLToPath(url: string | URL): string;
}

declare const process: { env: Record<string, string | undefined> };
