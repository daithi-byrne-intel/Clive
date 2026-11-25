# Levels and Test-Condition Language Inventory

## Production inputs

- `Shared/Common/LevelsSequences.lvl`
- `Shared/Common/BaseLevels.tcg`

Both files begin with `Version 1.0;` and are authoritative inputs for the combined viewer.

## `.lvl` structure

A file contains ordered `Levels <name> { ... }` declarations. A level body contains ordered entries of two observed forms:

1. Resource or pin-group entry:
   - identifier followed by a braced body;
   - body contains `<property> = <expression>;` assignments;
   - expressions include numbers, engineering-unit literals, booleans, quoted strings, identifiers, and formulas.
2. `SequenceBreak <expression>;` entry.

Whitespace, blank lines, original expression spelling, engineering-unit casing, declaration order, and unknown syntax must be retained. The grid row order is the source order, including sequence breaks. Grid columns are the union of properties used by the selected level.

## `.tcg` structure

The production file contains three ordered declaration kinds:

### SpecificationSet

`SpecificationSet <name> (<selector>, ...) { ... }`

The body contains typed variable declarations of the form `<type> <name> = <expression-list>;`. Observed values include scalar literals, comma-separated selector values, references, quoted strings, and formulas. Empty bodies are valid.

### TestConditionGroup

`TestConditionGroup <name> { SpecificationSet = <name>; Level = <qualified-level>; }`

The `Level` value is an explicit qualified reference such as `__main__::EDM_VSIM`. This is the authoritative TCG-to-level link; suffix or filename conventions must not be used for semantic resolution.

### TestCondition

`TestCondition <name> { TestConditionGroup = <name>; Selector = <selector>; }`

A test condition selects one selector from the group's specification set.

## Semantic chain

`TestCondition` -> `TestConditionGroup` -> (`SpecificationSet`, `Levels` declaration)

For a selected level, the viewer should find all groups whose `Level` reference resolves to it, then expose each group's specification set and its test conditions/selectors. Resolution removes an optional namespace prefix through the last `::` and compares the remaining identifier exactly. Duplicate declarations, missing references, and selectors absent from a specification set are diagnostics.

## Editing and preservation rules

- No-op parse/serialize must return the original bytes.
- A scalar grid edit changes only the source span containing that expression.
- Expressions are opaque text in the first editing release; they are not evaluated or normalized.
- Invalid source text remains visible while the table retains the last valid semantic snapshot.
- Unknown declarations or statements are retained and reported, never discarded.
- Comments beginning with `#` are supported as trivia; string contents are not interpreted as comments.

## Representative production locations

- `.lvl` resource rows and sequence breaks: `LevelsSequences.lvl` lines 4-18.
- `.tcg` selector/value lists: `BaseLevels.tcg` lines 20-27.
- TCG-to-level links: `BaseLevels.tcg` lines 9394-9427.
- Test-condition selector links: `BaseLevels.tcg` lines 10102-10141.
