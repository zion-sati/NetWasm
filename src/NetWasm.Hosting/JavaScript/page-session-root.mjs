// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

import { createBrowserRawExportSession } from "./browser-raw-export-session.mjs";
import { createPageSessionFactory } from "./page-session.mjs";

export const createPageSession = createPageSessionFactory(createBrowserRawExportSession);
