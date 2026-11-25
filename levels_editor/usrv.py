"""Read-only index of Torch ``.usrv`` (UserVars / SelectorRuleCollection) files.

Only variable definitions are indexed. Anything unrecognised is skipped and
recorded in ``warnings`` rather than aborting, so an odd file never blocks the
editor. Lines are 1-based.
"""
import re

_BLOCK = re.compile(r"^(UserVars|SelectorRuleCollection)(?:\s+(\w+))?\s*$")
_VAR = re.compile(r'^(\s*(\w+)\s+([A-Za-z_]\w*)\s*=\s*)("[^"]*"|[^;]*?)(\s*;)')
_NUM = r"[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?"
_UNITS = {"Voltage": ("V", "mV", "uV", "nV", "kV"), "Current": ("A", "mA", "uA", "nA")}
EDITABLE_TYPES = ("String", "Integer", "Double", "Boolean", "Voltage", "Current")


def normalize(type_, text):
    """Validated value text as it should be written to the file, or ValueError."""
    t = text.strip()
    if type_ == "String":
        inner = t[1:-1] if len(t) >= 2 and t[0] == t[-1] == '"' else t
        if '"' in inner or "\n" in inner or "\r" in inner:
            raise ValueError("a String value cannot contain a double quote or a line break")
        return '"%s"' % inner
    if type_ == "Integer":
        if not re.match(r"^[+-]?\d+$", t):
            raise ValueError("%r is not a whole number" % t)
        return t
    if type_ == "Double":
        if not re.match("^%s$" % _NUM, t):
            raise ValueError("%r is not a number" % t)
        return t
    if type_ == "Boolean":
        if t.upper() not in ("TRUE", "FALSE"):
            raise ValueError("a Boolean must be TRUE or FALSE")
        return t.upper()
    if type_ in _UNITS:
        if not re.match("^%s(?:%s)$" % (_NUM, "|".join(_UNITS[type_])), t):
            raise ValueError("%r is not a %s value (number plus one of %s)" % (t, type_, ", ".join(_UNITS[type_])))
        return t
    raise ValueError("editing %s uservars is not supported" % type_)


class UsrvVariable(object):
    def __init__(self, group, name, type_, value, line):
        self.group, self.name, self.type, self.value, self.line = group, name, type_, value, line
        self.original = value

    @property
    def modified(self):
        return self.value != self.original

    def edit_problem(self):
        """Why this variable can't be edited safely, or None if it can."""
        if self.type not in EDITABLE_TYPES:
            return "%s uservars can't be edited here" % self.type
        if self.type == "String" and not re.match(r'^"[^"]*"$', self.value):
            return "the current value is not a simple quoted string"
        try:
            normalize(self.type, self.value)
        except ValueError:
            return "the current value (%s) is an expression or unsupported form" % self.value
        return None

    @property
    def qualified(self):
        return "%s.%s" % (self.group, self.name) if self.group else self.name


class UsrvIndex(object):
    def __init__(self, text):
        self.by_qualified = {}
        self.by_name = {}
        self.warnings = []
        self._full = text.splitlines(True)  # keeps every line ending as read
        self._parse(text.splitlines())

    def text(self):
        return "".join(self._full)

    def is_modified(self):
        return any(v.modified for vs in self.by_name.values() for v in vs)

    def set_value(self, var, new_text):
        """Rewrite only the value text on the variable's line; returns the stored value."""
        problem = var.edit_problem()
        if problem:
            raise ValueError(problem)
        stored = normalize(var.type, new_text)
        full = self._full[var.line - 1]
        plain = full.rstrip("\r\n")
        m = _VAR.match(plain)
        if not m or m.group(4) != var.value:
            raise ValueError("line %d no longer matches the loaded definition" % var.line)
        self._full[var.line - 1] = plain[:m.start(4)] + stored + plain[m.end(4):] + full[len(plain):]
        var.value = stored
        return stored

    def _parse(self, lines):
        kind, group, depth, expect_brace = None, None, 0, False
        for no, raw in enumerate(lines, 1):
            line = raw.strip()
            if not line or line.startswith("#") or line.startswith("Version"):
                continue
            if kind is None:
                m = _BLOCK.match(line)
                if m:
                    kind, group, depth, expect_brace = m.group(1), m.group(2) or "", 0, True
                else:
                    self.warnings.append((no, line))
                continue
            if line == "{":
                depth += 1
                expect_brace = False
                continue
            if expect_brace:
                self.warnings.append((no, line))
                kind = None
                continue
            if line == "}":
                depth -= 1
                if depth <= 0:
                    kind = None
                continue
            if kind != "UserVars" or depth != 1:
                continue
            m = _VAR.match(raw)
            if not m:
                self.warnings.append((no, line))
                continue
            var = UsrvVariable(group, m.group(3), m.group(2), m.group(4), no)
            if group:
                self.by_qualified.setdefault(var.qualified, var)
            self.by_name.setdefault(var.name, []).append(var)
