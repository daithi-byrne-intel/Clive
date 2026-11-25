"""Read-only, loss-aware reader for Torch ``.tcg`` (test condition) files.

Provides the TestCondition -> TestConditionGroup -> (SpecificationSet,
Level) links used to resolve the variables referenced by ``.lvl`` values.
"""
import re

_VERSION = re.compile(r"^\s*Version\s+\S+;\s*$")
_BLOCK = re.compile(
    r"^(SpecificationSet|TestConditionGroup|TestCondition)\s+(\S+?)\s*(?:\(([^)]*)\))?\s*$"
)
_VAR = re.compile(r"^\s*([A-Za-z_]\w*)\s+([A-Za-z_]\w*)\s*=\s*(.*?)\s*;\s*$")
_ASSIGN = re.compile(r"^\s*([A-Za-z_]\w*)\s*=\s*(.*?)\s*;\s*$")


class TcgParseError(ValueError):
    def __init__(self, line_no, message):
        super(TcgParseError, self).__init__("line %d: %s" % (line_no, message))
        self.line_no = line_no


class Variable(object):
    def __init__(self, type_, name, values, line=0):
        self.type, self.name, self.values, self.line = type_, name, values, line

    def value_for(self, selector_index):
        if len(self.values) == 1:
            return self.values[0]
        if 0 <= selector_index < len(self.values):
            return self.values[selector_index]
        return None


class SpecificationSet(object):
    def __init__(self, name, selectors):
        self.name, self.selectors = name, selectors
        self.variables = {}


class TestConditionGroup(object):
    def __init__(self, name):
        self.name = name
        self.spec_set = None
        self.level = None


class TestCondition(object):
    def __init__(self, name):
        self.name = name
        self.group = None
        self.selector = None


class TcgDocument(object):
    def __init__(self, text):
        self.spec_sets, self.groups, self.conditions = {}, {}, {}
        self.condition_names = []
        self._parse(text.splitlines())

    def _parse(self, lines):
        i, n = 0, len(lines)
        while i < n:
            line = lines[i]
            if not line.strip() or line.lstrip().startswith("//") or _VERSION.match(line):
                i += 1
                continue
            m = _BLOCK.match(line)
            if not m:
                raise TcgParseError(i + 1, "unsupported construct %r" % line.strip())
            kind, name, args = m.groups()
            if i + 1 >= n or lines[i + 1].strip() != "{":
                raise TcgParseError(i + 2, "expected '{' after %s %s" % (kind, name))
            end = i + 2
            while end < n and lines[end].strip() != "}":
                end += 1
            if end >= n:
                raise TcgParseError(n, "unterminated %s %s" % (kind, name))
            self._build(kind, name, args, lines[i + 2:end], i + 3)
            i = end + 1

    def _build(self, kind, name, args, body, first_line_no):
        if kind == "SpecificationSet":
            obj = SpecificationSet(name, [s.strip() for s in (args or "").split(",") if s.strip()])
            for off, line in enumerate(body):
                if not line.strip() or line.lstrip().startswith("//"):
                    continue
                m = _VAR.match(line)
                if not m:
                    raise TcgParseError(first_line_no + off, "unsupported construct %r" % line.strip())
                obj.variables[m.group(2)] = Variable(
                    m.group(1), m.group(2), [v.strip() for v in m.group(3).split(",")],
                    first_line_no + off
                )
            self.spec_sets[name] = obj
            return
        fields = {}
        for off, line in enumerate(body):
            if not line.strip() or line.lstrip().startswith("//"):
                continue
            m = _ASSIGN.match(line)
            if not m:
                raise TcgParseError(first_line_no + off, "unsupported construct %r" % line.strip())
            fields[m.group(1)] = m.group(2)
        if kind == "TestConditionGroup":
            obj = TestConditionGroup(name)
            obj.spec_set = fields.get("SpecificationSet")
            level = fields.get("Level")
            obj.level = level.split("::")[-1] if level else None
            self.groups[name] = obj
        else:
            obj = TestCondition(name)
            obj.group = fields.get("TestConditionGroup")
            obj.selector = fields.get("Selector")
            self.conditions[name] = obj
            self.condition_names.append(name)

    def resolve(self, condition_name):
        """Return (condition, group, spec_set, level_name, variables) for a TestCondition."""
        tc = self.conditions[condition_name]
        group = self.groups.get(tc.group)
        spec = self.spec_sets.get(group.spec_set) if group else None
        level = group.level if group else None
        variables = {}
        if spec:
            idx = spec.selectors.index(tc.selector) if tc.selector in spec.selectors else -1
            for var in spec.variables.values():
                variables[var.name] = var.value_for(idx)
        return tc, group, spec, level, variables
