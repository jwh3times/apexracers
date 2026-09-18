// Restores the jest-dom matcher types under Vitest 5.
//
// The matchers themselves work fine — `src/test/setup.ts` imports `@testing-library/jest-dom`,
// which registers them at runtime, and the whole suite passes. What broke is only TypeScript's
// knowledge of them, so `tsc` fails the production build while the tests are green.
//
// Vitest 5 inlined the `expect` package (vitest-dev/vitest#10221) and its `Assertion` now takes two
// type parameters, `Assertion<R extends void | Promise<void>, T>`. `@testing-library/jest-dom` still
// augments it with one — `interface Assertion<T = any>` in its `types/vitest.d.ts`. Declaration
// merging requires identical type-parameter lists, so that augmentation silently stops attaching
// and every matcher disappears from the type. The failure is quiet in the worst way: nothing warns
// that an augmentation did not apply, it just reports `Property 'toBeInTheDocument' does not exist`
// at all ~797 call sites.
//
// This augments `Matchers` rather than `Assertion`, because in Vitest 5 that is the single
// extension point the others are built on — `Assertion<R, T>`, `AsymmetricMatchersContaining` and
// `ExpectStatic` all extend `Matchers`, so one declaration covers `expect(...)`, `expect.not` and
// the asymmetric matchers together. The type parameters must match Vitest's own declaration exactly,
// defaults included, or the merge fails the same silent way.
//
// Upstream tracks this as testing-library/jest-dom#738, open since 2026-09-06 with no fix released;
// 7.0.1 is the latest published version. **Delete this file when jest-dom ships Vitest 5 support**
// and confirm `npm run build` still passes — if the augmentation has landed upstream, this one
// becomes redundant rather than harmful, but there is no reason to keep two.
import type { TestingLibraryMatchers } from '@testing-library/jest-dom/matchers';

// The empty body is the point: this is a declaration merge that contributes jest-dom's matchers to
// Vitest's own `Matchers`, so it adds members by extension and must declare none itself. Giving it a
// body to satisfy `no-empty-object-type` would change what it merges. The disable is a block rather
// than `-next-line` because the signature wraps across several lines and the rule reports at the
// `{}`, not at the `interface` keyword.
/* oxlint-disable typescript/no-empty-object-type */
declare module 'vitest' {
  interface Matchers<
    R extends void | Promise<void> = void | Promise<void>,
    T = unknown,
  > extends TestingLibraryMatchers<unknown, R> {}
}
/* oxlint-enable typescript/no-empty-object-type */
