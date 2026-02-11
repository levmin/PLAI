# PLAI

## Provenance

This project’s source code was generated with the assistance of AI systems.
The project maintainer does not claim human authorship of individual source files.
PLAI is distributed as a collective work under the Apache License 2.0.

## Development Model

PLAI is developed in a fully agentic workflow.

All architecture decisions are explicitly defined and versioned before implementation.

The model manifest is the authoritative source of supported models for each release.

## Overview

PLAI is a Windows desktop application (WPF, .NET 10) that enables deterministic local AI inference using ONNX-based models.

PLAI automatically detects available hardware (system RAM, discrete GPU presence, VRAM) and selects the most appropriate supported local model according to a fixed, reviewable policy.

All model metadata is defined in a versioned, embedded JSON manifest included in the application.

---

## Supported Platform

- Windows 10 (19041+)
- Windows 11
- Packaged as MSIX
- No administrative privileges required
- No CUDA or external AI runtime required

---

## PLAI v1 Feature Scope

### ✔ Hardware Detection

On first launch, PLAI detects:

- System RAM
- Presence of discrete GPU
- Available VRAM

If no supported model fits available resources, PLAI informs the user and exits.

---

### ✔ Deterministic Model Selection

Model selection is governed by a fixed rule:

1. Filter models that satisfy:
   - `systemRam >= minRamGb`
   - For GPU models: discrete GPU present AND `vram >= minVramGb`

2. From eligible models:
   - Select highest `minVramGb`
   - If tied, select highest `minRamGb`

Row order and model family do not influence selection.

---

### ✔ Authoritative Model Manifest

- Model catalog is defined in a single embedded JSON file.
- No models are hardcoded in source code.
- No runtime parsing of external CSV or Excel files.
- Manifest version is frozen for v1.

---

### ✔ First-Run Behavior

On first launch:

- Selected model is shown to the user.
- Model is downloaded from the manifest-defined URL.
- Download progress is displayed.
- Model is stored in package-local storage.

If download is cancelled or fails:

- Partial files are removed.
- Persisted selection is cleared.
- Next launch behaves as first run.

---

### ✔ Storage Model

Models are stored in LocalState/Models/{model-id}

A model is considered complete if:

- `model.onnx` exists
- File size > 0

No hashing or integrity validation is performed in v1.

---

### ✔ Self-Healing Behavior

If a downloaded model is deleted or corrupted:

- PLAI detects the missing file at startup.
- Persisted selection is cleared.
- Application behaves as first run.

---

### ✔ Clean Uninstall

Because PLAI is packaged as MSIX:

- All application data, including downloaded models, is removed automatically on uninstall.
- No manual registry cleanup is required.

---

## Not Included in v1

PLAI v1 intentionally does NOT include:

- Multiple model switching
- Background downloads
- Hash verification or integrity validation
- Model auto-updates
- GPU optimization tuning
- Benchmarking features
- Cloud inference fallback
- Telemetry

Future versions may expand capabilities.

---

