## What this changes

<!-- A short description of the change and the problem it solves. -->

## Why

<!-- Context or a linked issue. For terminal behaviour, cite the relevant sequence or standard
     (ECMA-48, DEC STD 070, the xterm control sequences document) where it helps a reviewer. -->

Closes #

## How it was verified

<!-- Tests added, and any manual verification: which shell, which command, what you saw. -->

- [ ] `dotnet build -warnaserror` is clean
- [ ] `dotnet test` passes
- [ ] `dotnet format --verify-no-changes` passes
- [ ] Tests were added or updated for the behaviour changed
- [ ] Parser changes include a test feeding the sequence split across multiple chunks
- [ ] `CHANGELOG.md` updated under `[Unreleased]` (for user-visible changes)

## Architectural impact

<!-- Does this add a dependency between projects? If so, LayeringTests must be updated in this same
     pull request, with an explanation of why the new edge is correct. -->

- [ ] No new dependency edges between projects
- [ ] No GUI dependency added to the terminal engine
