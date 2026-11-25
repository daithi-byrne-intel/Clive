"""Evaluates ``.lvl`` value text (literals with units, strings, variables, + - * /)
to a display value, resolving variables from the spec set. Never uses eval()."""
import re

UNITS = {
    "": 1.0,
    "V": 1.0, "mV": 1e-3, "uV": 1e-6, "kV": 1e3,
    "A": 1.0, "mA": 1e-3, "uA": 1e-6, "nA": 1e-9,
    "S": 1.0, "s": 1.0, "mS": 1e-3, "ms": 1e-3, "uS": 1e-6, "us": 1e-6, "nS": 1e-9, "ns": 1e-9,
    "Hz": 1.0, "kHz": 1e3, "MHz": 1e6, "GHz": 1e9,
    "Ohm": 1.0, "kOhm": 1e3,
}

_TOKEN = re.compile(
    r"\s*(?:(?P<num>(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)(?P<unit>[A-Za-z]*)"
    r"|(?P<name>[A-Za-z_]\w*)|(?P<op>[-+*/()]))"
)
_MAX_DEPTH = 16


class EvalError(ValueError):
    pass


def _tokenize(text):
    pos, out = 0, []
    text = text.rstrip()
    while pos < len(text):
        m = _TOKEN.match(text, pos)
        if not m:
            raise EvalError("bad token in %r" % text)
        pos = m.end()
        if m.group("num") is not None:
            unit = m.group("unit")
            if unit not in UNITS:
                raise EvalError("unknown unit %r" % unit)
            out.append(("num", float(m.group("num")) * UNITS[unit]))
        elif m.group("name") is not None:
            out.append(("name", m.group("name")))
        else:
            out.append(("op", m.group("op")))
    return out


class _Parser(object):
    def __init__(self, tokens, variables, depth):
        self.t, self.i, self.vars, self.depth = tokens, 0, variables, depth

    def peek(self):
        return self.t[self.i] if self.i < len(self.t) else (None, None)

    def take(self):
        tok = self.peek()
        self.i += 1
        return tok

    def expr(self):
        v = self.term()
        while self.peek() in (("op", "+"), ("op", "-")):
            op = self.take()[1]
            r = self.term()
            v = v + r if op == "+" else v - r
        return v

    def term(self):
        v = self.factor()
        while self.peek() in (("op", "*"), ("op", "/")):
            op = self.take()[1]
            r = self.factor()
            if op == "*":
                v = v * r
            else:
                if r == 0:
                    raise EvalError("divide by zero")
                v = v / r
        return v

    def factor(self):
        kind, val = self.take()
        if kind == "num":
            return val
        if kind == "name":
            return evaluate_number(self.vars.get(val), self.vars, self.depth + 1)
        if (kind, val) == ("op", "-"):
            return -self.factor()
        if (kind, val) == ("op", "("):
            v = self.expr()
            if self.take() != ("op", ")"):
                raise EvalError("missing )")
            return v
        raise EvalError("unexpected token")


def evaluate_number(text, variables, depth=0):
    if text is None or depth > _MAX_DEPTH:
        raise EvalError("unresolved")
    p = _Parser(_tokenize(text), variables, depth)
    v = p.expr()
    if p.i != len(p.t):
        raise EvalError("trailing tokens")
    return v


def display_value(text, variables):
    """Value shown next to an expression; '' when it cannot be resolved."""
    text = text.strip()
    if len(text) >= 2 and text[0] == '"' and text[-1] == '"':
        return text[1:-1]
    if text.upper() in ("TRUE", "FALSE"):
        return text.upper().capitalize()
    try:
        return "%g" % evaluate_number(text, variables)
    except EvalError:
        return ""
    except (ArithmeticError, TypeError):
        return ""
