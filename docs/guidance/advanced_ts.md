# Advanced TypeScript Guide

## Philosophy

* TypeScript is more than just types for JavaScript — it’s a **type system for modeling intent**.
* Strive for **expressive, type-safe APIs** without overcomplicating.
* Remember that type safety is for **developer productivity**, not runtime.
* Prefer **clarity over type gymnastics** unless complexity truly pays off.

## Type-Level Programming

* Use **conditional types** for flexibility.

```ts
type ApiResponse<T> = T extends Error ? { error: string } : { data: T };
```

* Leverage **mapped types** for transformations.

```ts
type ReadonlyExcept<T, K extends keyof T> = {
  readonly [P in keyof T as P extends K ? never : P]: T[P];
} & Pick<T, K>;
```

* Use **template literal types** to model string contracts.

```ts
type Route = `/api/${string}`;
```

## Generics & Inference Tricks

* Use generics to create reusable utilities.

```ts
function identity<T>(value: T): T { return value; }
```

* Infer return types automatically with `infer`.

```ts
type ReturnTypeAsync<T> = T extends (...args: any[]) => Promise<infer R> ? R : never;
```

* Avoid over-generic signatures when concrete types are simpler.

## Utility Types

* Built-in utility types: `Partial<T>`, `Required<T>`, `Readonly<T>`, `Pick<T>`, `Omit<T>`.
* Create custom utilities where needed.

```ts
type NonNullableProps<T> = {
  [K in keyof T]: NonNullable<T[K]>;
};
```

## Discriminated Unions

* Use unions for exhaustive checks.

```ts
type Shape =
  | { kind: "circle"; radius: number }
  | { kind: "square"; size: number };

function area(s: Shape): number {
  switch (s.kind) {
    case "circle": return Math.PI * s.radius ** 2;
    case "square": return s.size * s.size;
    default: const _exhaustive: never = s; return _exhaustive;
  }
}
```

## Branded Types

* Prevent accidental misuse of primitive types.

```ts
type UserId = string & { __brand: "UserId" };
function makeUserId(id: string): UserId { return id as UserId; }
```

## Fluent APIs & Builders

* Create safe builders with chained calls.

```ts
class QueryBuilder<T = {}> {
  private state: T;
  constructor(state: T = {} as T) { this.state = state; }
  select<K extends string>(field: K) {
    return new QueryBuilder<T & { select: K }>( {...this.state, select: field });
  }
}

const q = new QueryBuilder().select("name");
```

## The `satisfies` Operator

* Enforce constraints without widening types.

```ts
const config = {
  apiUrl: "https://example.com",
  retries: 3,
} satisfies Record<string, string | number>;
```

## Decorators & Metaprogramming

* Use class decorators for metadata, but remember they are **experimental**.

```ts
function Controller(prefix: string) {
  return (target: Function) => Reflect.defineMetadata("prefix", prefix, target);
}
```
