#!/usr/bin/env python3
"""
Pre-compile checks for BubbleGlass.cs.

The point is not to replace the compiler (it cannot). It catches the two
mistakes that have actually shipped a broken build:

  CS0136  the same local name declared at two nesting levels of one method
          (C# rejects this in either order)
  CS0128  the same name declared twice in one scope

Both are pure name/brace problems, so they can be found without type
information. Everything else is left to csc.exe.

Scoping is modelled with a "block chain": the ordered list of lines that
opened the blocks enclosing a declaration. Two declarations share a scope
only when their chains are identical; one is nested in the other only when
one chain is a strict prefix of the other. Sibling blocks therefore never
collide, which is what keeps repeated `for (int i ...)` loops quiet.

Usage:  python tools/preflight.py [file]
Exit:   0 clean, 1 findings, 2 the file could not be read
"""

import re
import sys


def strip_comments_and_strings(src):
    """Blank out comments and literals but keep newlines, so line numbers hold."""
    out = []
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        if c == '/' and i + 1 < n and src[i + 1] == '/':
            while i < n and src[i] not in '\r\n':
                out.append(' ')
                i += 1
        elif c == '/' and i + 1 < n and src[i + 1] == '*':
            while i + 1 < n and not (src[i] == '*' and src[i + 1] == '/'):
                out.append('\n' if src[i] == '\n' else ' ')
                i += 1
            out.append('  ')
            i += 2
        elif c == '"':
            out.append(' ')
            i += 1
            while i < n:
                if src[i] == '\\':
                    out.append('  ')
                    i += 2
                    continue
                if src[i] == '"':
                    out.append(' ')
                    i += 1
                    break
                out.append('\n' if src[i] == '\n' else ' ')
                i += 1
        elif c == "'":
            out.append(' ')
            i += 1
            while i < n:
                if src[i] == '\\':
                    out.append('  ')
                    i += 2
                    continue
                if src[i] == "'":
                    out.append(' ')
                    i += 1
                    break
                out.append(' ')
                i += 1
        else:
            out.append(c)
            i += 1
    return ''.join(out)


def check_braces(code):
    stack = []
    pairs = {')': '(', '}': '{', ']': '['}
    line = 1
    for ch in code:
        if ch == '\n':
            line += 1
        elif ch in '({[':
            stack.append((ch, line))
        elif ch in ')}]':
            if not stack or stack[-1][0] != pairs[ch]:
                top = stack[-1] if stack else None
                return ['CS1002/CS1513: unmatched %r on line %d (innermost open: %s)'
                        % (ch, line, top)]
            stack.pop()
    if stack:
        return ['CS1513: %d unclosed bracket(s), first at line %d %r'
                % (len(stack), stack[0][1], stack[0][0])]
    return []


CTRL = {'if', 'for', 'while', 'switch', 'catch', 'using', 'lock', 'foreach',
        'return', 'new', 'else', 'do', 'try', 'finally', 'throw', 'case'}

METHOD_DECL = re.compile(
    r'^\s{4,12}(?:(?:public|private|protected|internal|static|override|virtual'
    r'|sealed|extern|unsafe)\s+)*[\w<>\[\]\.\,]+\s+([A-Za-z_]\w*)\s*\(')

TYPE = (r'(?:int|uint|long|float|double|bool|byte|string|var|char|object'
        r'|IntPtr|Color|Sprite|Bubble|Size|Rectangle|Point|Nat\.POINT)')

DECL = re.compile(r'^\s*(?:const\s+)?(?:' + TYPE + r')\s+([A-Za-z_]\w*)\s*(?:=|;|,|\))')

# declarations whose scope is the statement's own block, not the enclosing one
STATEMENT_DECL = [
    re.compile(r'^\s*for\s*\(\s*' + TYPE + r'\s+([A-Za-z_]\w*)'),
    re.compile(r'^\s*foreach\s*\(\s*' + TYPE + r'\s+([A-Za-z_]\w*)'),
    re.compile(r'^\s*using\s*\(\s*' + TYPE + r'\s+([A-Za-z_]\w*)'),
]
CATCH_DECL = re.compile(r'\bcatch\s*\(\s*[\w\.<>\[\]]+\s+([A-Za-z_]\w*)\s*\)')


def block_span(lines, idx):
    """(line that opens the body brace, line that closes it), or (None, None)."""
    net, open_line = 0, None
    for j in range(idx, len(lines)):
        o, c = lines[j].count('{'), lines[j].count('}')
        if o and open_line is None:
            open_line = j
        net += o - c
        if open_line is not None and net <= 0:
            return open_line, j
        if open_line is None and ';' in lines[j]:
            return None, None                 # abstract / expression-bodied
    return None, None


def find_methods(lines):
    """body brace line -> (closing line, method name)"""
    methods = {}
    for idx, ln in enumerate(lines):
        m = METHOD_DECL.match(ln)
        if not m or m.group(1) in CTRL:
            continue
        open_line, end = block_span(lines, idx)
        if open_line is not None:
            methods[open_line] = (end, m.group(1))
    return methods


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else 'BubbleGlass.cs'
    try:
        with open(path, encoding='utf-8') as f:
            src = f.read()
    except OSError as e:
        print('[preflight] cannot read %s: %s' % (path, e))
        return 2

    code = strip_comments_and_strings(src)
    lines = code.split('\n')

    problems = check_braces(code)
    if problems:
        print('[preflight] %d problem(s) in %s:' % (len(problems), path))
        for p in problems:
            print('  ' + p)
        return 1

    # block chain per line: tuple of "block opening" identifiers
    chains = []
    stack = []
    for idx, ln in enumerate(lines):
        chains.append(tuple(stack))
        for ch in ln:
            if ch == '{':
                stack.append(('b', idx))
            elif ch == '}':
                if stack:
                    stack.pop()

    methods = find_methods(lines)          # body_brace_line -> (end, name)

    def owning_method(chain):
        for ident in reversed(chain):
            if ident[0] == 'b' and ident[1] in methods:
                return ident[1]
        return None

    decls = []                              # (name, line_no, chain, method_line)
    for idx, ln in enumerate(lines):
        name = None
        chain = chains[idx]
        for pat in STATEMENT_DECL:
            m = pat.match(ln)
            if m:
                name = m.group(1)
                chain = chains[idx] + (('s', idx),)   # own little scope
                break
        if name is None:
            m = CATCH_DECL.search(ln)
            if m:
                name = m.group(1)
                chain = chains[idx] + (('s', idx),)
        if name is None:
            m = DECL.match(ln)
            if not m or m.group(1) in CTRL:
                continue
            name = m.group(1)
            if re.search(r'\b' + re.escape(name) + r'\s*\(', ln):
                continue                    # that is a method signature
            chain = chains[idx]

        ml = owning_method(chain)
        if ml is None:
            continue                        # field / class level: different rules
        decls.append((name, idx + 1, chain, ml))

    findings = []

    # CS0128 -- identical chain, same name
    seen = {}
    for name, line_no, chain, ml in decls:
        key = (name, chain)
        if key in seen:
            findings.append('CS0128: "%s" declared twice in one scope, lines %d and %d'
                            % (name, seen[key], line_no))
        else:
            seen[key] = line_no

    # CS0136 -- same name, one chain a strict prefix of the other
    for a in range(len(decls)):
        for b in range(a + 1, len(decls)):
            na, la, ca, ma = decls[a]
            nb, lb, cb, mb = decls[b]
            if na != nb or ma != mb:
                continue
            if len(ca) == len(cb):
                continue
            shal, deep = (a, b) if len(ca) < len(cb) else (b, a)
            sn, sl, sc, _ = decls[shal]
            dn, dl, dc, _ = decls[deep]
            if dc[:len(sc)] == sc:
                findings.append('CS0136: "%s" at line %d is also declared at line %d '
                                'in an enclosing scope of the same method (%s)'
                                % (sn, dl, sl, methods[ma][1]))

    if findings:
        print('[preflight] %d problem(s) in %s:' % (len(findings), path))
        for p in findings:
            print('  ' + p)
        print('[preflight] fix these before the compiler sees the file.')
        return 1

    print('[preflight] OK  (%d lines, %d methods, %d locals checked)'
          % (len(lines), len(methods), len(decls)))
    return 0


if __name__ == '__main__':
    # Exit codes: 0 clean, 1 findings, 2 cannot read, 3 this script broke.
    # build.bat only treats 1 as fatal - anything else must not block a build.
    try:
        sys.exit(main())
    except SystemExit:
        raise
    except Exception as exc:                      # noqa: BLE001
        print('[preflight] internal error: %r' % (exc,))
        sys.exit(3)
