"""Loss-aware reader/editor for Torch ``.lvl`` (Levels) files.

The document keeps every original line (including line endings). Parsed
nodes only point at lines, and an edit replaces the value text of a single
line, so parse -> write is byte-identical and edits are targeted.
"""
import re

_VERSION = re.compile(r"^\s*Version\s+\S+;\s*$")
_LEVELS = re.compile(r"^Levels\s+(\S+)\s*$")
_BREAK = re.compile(
    r"^(?P<indent>[ \t]*)SequenceBreak(?P<eq>[ \t]+)(?P<value>[^;]*?)(?P<tail>[ \t]*;[ \t]*)$"
)
_PROP = re.compile(
    r"^(?P<indent>[ \t]*)(?P<name>[A-Za-z_]\w*)(?P<eq>[ \t]*=[ \t]*)(?P<value>.*?)(?P<tail>[ \t]*;[ \t]*)$"
)
_IDENT = re.compile(r"^[ \t]*([A-Za-z_]\w*)[ \t]*$")
_BAD_VALUE = re.compile(r"[;\r\n{}]")


class LvlParseError(ValueError):
    def __init__(self, line_no, message):
        super(LvlParseError, self).__init__("line %d: %s" % (line_no, message))
        self.line_no = line_no


class Property(object):
    """One ``Name = value;`` (or ``SequenceBreak value;``) line."""

    def __init__(self, line_idx, indent, name, eq, value, tail, eol):
        self.line_idx = line_idx
        self.indent, self.name, self.eq = indent, name, eq
        self.value, self.tail, self.eol = value, tail, eol
        self.original = value

    @property
    def modified(self):
        return self.value != self.original

    def render(self):
        return self.indent + self.name + self.eq + self.value + self.tail + self.eol


class Row(object):
    """A resource block (``kind == 'resource'``) or a SequenceBreak row."""

    def __init__(self, kind, name, props, break_prop=None):
        self.kind = kind
        self.name = name
        self.props = props  # name -> Property, in file order
        self.break_prop = break_prop


class LevelSet(object):
    def __init__(self, name, rows):
        self.name = name
        self.rows = rows

    def property_names(self):
        seen = []
        for row in self.rows:
            for name in row.props:
                if name not in seen:
                    seen.append(name)
        return seen


def _split_eol(raw):
    body = raw.rstrip("\r\n")
    return body, raw[len(body):]


class LvlDocument(object):
    def __init__(self, text):
        self.lines = text.splitlines(True)
        self.level_sets = []
        self._parse()
        self.by_name = dict((ls.name, ls) for ls in self.level_sets)

    def text(self):
        return "".join(self.lines)

    def _body(self, idx):
        return _split_eol(self.lines[idx])[0]

    def _skip_blank(self, idx):
        n = len(self.lines)
        while idx < n:
            stripped = self._body(idx).strip()
            if stripped and not stripped.startswith("//"):
                break
            idx += 1
        return idx

    def _parse(self):
        n = len(self.lines)
        i = self._skip_blank(0)
        if i < n and _VERSION.match(self._body(i)):
            i = self._skip_blank(i + 1)
        names = set()
        while i < n:
            m = _LEVELS.match(self._body(i))
            if not m:
                raise LvlParseError(i + 1, "expected 'Levels <name>', got %r" % self._body(i).strip())
            name = m.group(1)
            if name in names:
                raise LvlParseError(i + 1, "duplicate Levels name %r" % name)
            names.add(name)
            i = self._skip_blank(i + 1)
            if i >= n or self._body(i).strip() != "{":
                raise LvlParseError(i + 1, "expected '{' after Levels %s" % name)
            i, rows = self._parse_level_body(i + 1, name)
            self.level_sets.append(LevelSet(name, rows))
            i = self._skip_blank(i)

    def _parse_level_body(self, i, level_name):
        n = len(self.lines)
        rows = []
        while True:
            if i >= n:
                raise LvlParseError(n, "unterminated Levels %s" % level_name)
            text, eol = _split_eol(self.lines[i])
            stripped = text.strip()
            if not stripped or stripped.startswith("//"):
                i += 1
            elif stripped == "}":
                return i + 1, rows
            elif _BREAK.match(text):
                m = _BREAK.match(text)
                prop = Property(i, m.group("indent"), "SequenceBreak", m.group("eq"),
                                m.group("value"), m.group("tail"), eol)
                rows.append(Row("break", "SequenceBreak", {}, prop))
                i += 1
            elif _IDENT.match(text):
                res = _IDENT.match(text).group(1)
                if i + 1 >= n or self._body(i + 1).strip() != "{":
                    raise LvlParseError(i + 1, "expected '{' after resource %s" % res)
                i, props = self._parse_resource_body(i + 2, res)
                rows.append(Row("resource", res, props))
            else:
                raise LvlParseError(i + 1, "unsupported construct %r" % stripped)

    def _parse_resource_body(self, i, res):
        n = len(self.lines)
        props = {}
        order = []
        while True:
            if i >= n:
                raise LvlParseError(n, "unterminated resource %s" % res)
            text, eol = _split_eol(self.lines[i])
            stripped = text.strip()
            if not stripped or stripped.startswith("//"):
                i += 1
            elif stripped == "}":
                return i + 1, _OrderedProps(order, props)
            else:
                m = _PROP.match(text)
                if not m:
                    raise LvlParseError(i + 1, "unsupported construct %r" % stripped)
                name = m.group("name")
                if name in props:
                    raise LvlParseError(i + 1, "duplicate property %s in %s" % (name, res))
                props[name] = Property(i, m.group("indent"), name, m.group("eq"),
                                       m.group("value"), m.group("tail"), eol)
                order.append(name)
                i += 1

    def set_value(self, prop, value):
        """Replace the value text of an existing property line."""
        value = value.strip()
        if not value or _BAD_VALUE.search(value):
            raise ValueError("invalid value %r" % value)
        prop.value = value
        self.lines[prop.line_idx] = prop.render()

    def is_modified(self):
        for ls in self.level_sets:
            for row in ls.rows:
                props = [row.break_prop] if row.break_prop else list(row.props.values())
                if any(p.modified for p in props):
                    return True
        return False


class _OrderedProps(dict):
    """dict that iterates in file order (Python 3.3 dicts are unordered)."""

    def __init__(self, order, props):
        super(_OrderedProps, self).__init__(props)
        self._order = order

    def __iter__(self):
        return iter(self._order)

    def keys(self):
        return list(self._order)

    def values(self):
        return [self[k] for k in self._order]

    def items(self):
        return [(k, self[k]) for k in self._order]
