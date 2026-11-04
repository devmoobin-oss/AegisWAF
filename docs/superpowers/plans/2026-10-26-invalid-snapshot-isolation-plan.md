# Invalid persisted snapshot isolation plan

1. Add a red/green distributed integration regression around invalid persisted upstream data.
2. Build the test project with warnings treated as errors.
3. Run the distributed focused gate and confirm both peer activation and invalid-snapshot recovery.
4. Run the complete VSTest suite with a bounded wrapper and record exact counts.
5. Update `handoff.md` with before/after percentages, evidence, limits, and the next opportunity.
