import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

// Vitest запускается без globals, поэтому очистка DOM между тестами
// регистрируется явно.
afterEach(cleanup);
