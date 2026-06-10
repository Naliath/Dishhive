# Dishhive Documentation

This directory contains all planning, architecture, and feature documentation for the Dishhive application.

## Quick Links

| Document | Purpose |
|----------|---------|
| [Infrastructure Setup Plan](./infrastructure-setup-plan.md) | Repository structure, tech stack, Docker setup, port allocation, configuration |
| [Architecture Overview](./architecture.md) | System architecture, layer responsibilities, key decisions, integration patterns |
| [Future Features](./future-features.md) | Post-v1 capabilities inspired by Mealie and other meal planning tools |

## Feature Specifications

Each feature has its own specification document with user stories, domain model, requirements, and an implementation checklist.

| Feature | Document |
|---------|----------|
| Family Composition | [feature-family-composition.md](./features/feature-family-composition.md) |
| Week Planner | [feature-week-planner.md](./features/feature-week-planner.md) |
| Past Dishes & Statistics | [feature-past-dishes-statistics.md](./features/feature-past-dishes-statistics.md) |
| Freezy Integration | [feature-freezy-integration.md](./features/feature-freezy-integration.md) |
| Recipe Store | [feature-recipe-store.md](./features/feature-recipe-store.md) |
| Recipe Import | [feature-recipe-import.md](./features/feature-recipe-import.md) |
| Measurement Preferences | [feature-measurement-preferences.md](./features/feature-measurement-preferences.md) |
| Shopping List Export | [feature-shopping-list-export.md](./features/feature-shopping-list-export.md) |

## Checklist Status Legend

| Symbol | Meaning |
|--------|---------|
| `[ ]` | Not started |
| `[~]` | In progress |
| `[x]` | Completed |

## Conventions

This documentation follows the same conventions as the FreezerInventory (Freezy) project:

- One document per topic
- Implementation checklists with status tracking
- Living documents updated as work progresses
- Clear separation between planning and implementation notes

## How to Use These Documents

1. **Start with the [Infrastructure Setup Plan](./infrastructure-setup-plan.md)** to understand the project structure and tech stack.
2. **Review the [Architecture Overview](./architecture.md)** for system design decisions.
3. **Check feature documents** for detailed requirements and implementation status.
4. **Reference [Future Features](./future-features.md)** for post-v1 roadmap items.
