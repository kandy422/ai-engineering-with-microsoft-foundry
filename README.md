# AI Engineering with Microsoft Foundry

The companion learner package for Pixel Protocol's 16-module, 111-video course.
Build from AI and machine-learning foundations to a support assistant with grounded answers, scoped tools, explicit approvals, evaluation, deployment, traces and rollback.

**[Download the complete learner package (v1.0.0)](https://github.com/kandy422/ai-engineering-with-microsoft-foundry/releases/download/v1.0.0/AI-Engineering-with-Microsoft-Foundry-Learner-2026-11-15-8f9275fffa3a.zip)** · [Release and SHA-256 checksum](https://github.com/kandy422/ai-engineering-with-microsoft-foundry/releases/tag/v1.0.0)

## Start here

1. Download and extract the ZIP to a writable folder, such as `D:\AI-Course`. Open `START-HERE.html` in the extracted folder.
2. Follow the [Module 1 setup](modules/module-01/LEARNER-SETUP.md). The tested environment uses Windows, PowerShell 7, the .NET 10 SDK for C# applications, and Python 3.12 for early ML labs. Follow each module's exact versions and dependency instructions.
3. Run the baseline, predict the effect of the lesson's change, make the change, inspect the result, and complete the independent exercise.
4. Keep the module folders together. Later exercises reference earlier modules.

You can also clone this repository and open `START-HERE.html` locally. GitHub displays its HTML source; the module Markdown guides below are readable directly on GitHub.

## Modules

| Module | Guide |
| --- | --- |
| 01 | [What AI is and when to use it](modules/module-01/LEARNER-SETUP.md) |
| 02 | [Data and how models learn](modules/module-02/README.md) |
| 03 | [Build and evaluate a small ML model](modules/module-03/README.md) |
| 04 | [Neural networks and transformers](modules/module-04/README.md) |
| 05 | [How language models generate answers](modules/module-05/README.md) |
| 06 | [Prompts and dependable outputs](modules/module-06/README.md) |
| 07 | [First application with Microsoft Foundry](modules/module-07/README.md) |
| 08 | [Choose and adapt a model](modules/module-08/README.md) |
| 09 | [Embeddings and semantic search](modules/module-09/README.md) |
| 10 | [Retrieval-augmented generation](modules/module-10/README.md) |
| 11 | [Tools, workflows and agents](modules/module-11/README.md) |
| 12 | [Multimodal AI](modules/module-12/README.md) |
| 13 | [Evaluate and improve the whole system](modules/module-13/README.md) |
| 14 | [Security and responsible AI](modules/module-14/README.md) |
| 15 | [Deploy and operate the application](modules/module-15/README.md) |
| 16 | [End-to-end capstone](modules/module-16/README.md) |

## Local exercises and Azure exercises

The package includes runnable code, fictional teaching data, expected outcomes, checkpoints, and labelled instructor recordings or service responses. A saved response is evidence of an instructor run; it is not a live request from your machine.

Local exercises can be run before provisioning Azure resources. Live exercises require your own authorized Azure subscription, supported regional services, model access and Entra permissions. Follow each module's setup, cost controls and cleanup instructions. Azure usage may incur charges. Instructor course resources have been retired; historical endpoints are not shared learner services or credentials.

For the final project, start with [Module 16](modules/module-16/README.md) and its [hosted setup](modules/module-16/lab/Hosted-checkpoint.md). The course includes real deployed demonstrations, but passing teaching checkpoints is not a substitute for assessing a new application's security, reliability and operating costs.

## Videos

**[Watch the complete course on YouTube](https://www.youtube.com/playlist?list=PLLMgRqbMovSI)** — all 111 lessons are public and arranged in curriculum order. Start with the series overview and follow the module guides alongside the videos. Each video description links to the learner download and its module guide.

## Package integrity

Release `v1.0.0` contains all 16 modules: 592 learner files plus `START-HERE.html` and `package-manifest.json` (594 archive entries).

ZIP SHA-256:

```text
270ef89b2d955f795e86adf3704fa0db239c0610fe533e55a743c7bcfb1c79cf
```

To verify the download in PowerShell:

```powershell
Get-FileHash -Algorithm SHA256 ./AI-Engineering-with-Microsoft-Foundry-Learner-2026-11-15-8f9275fffa3a.zip
```

Module manifests record file provenance and checksums. Third-party notices remain with their respective files, including the bundled Microsoft Authentication Library notice. No additional license grant is introduced by this repository.
