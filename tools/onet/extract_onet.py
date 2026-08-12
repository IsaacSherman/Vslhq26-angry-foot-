"""Extract a curated benchmark subset from O*NET OnLine for the AngryFoot occupation benchmark.

Pulls, per SOC code:
  - occupation title + "Sample of reported job titles" (used for title -> SOC mapping)
  - Transferable Skills / Essential Skills / Knowledge with O*NET's published 0-100 Importance
  - Technology Skills flagged Hot / In-Demand by O*NET

All values are transcribed verbatim from O*NET OnLine; nothing is invented.
"""

import csv
import html
import io
import json
import re
import sys
import time
import urllib.parse
import urllib.request

BASE = "https://www.onetonline.org"
UA = {"User-Agent": "Mozilla/5.0 (AngryFoot benchmark dataset build)"}

SOCS = [
    "15-1252.00",  # Software Developers
    "15-1251.00",  # Computer Programmers
    "15-1253.00",  # Software Quality Assurance Analysts and Testers
    "15-1254.00",  # Web Developers
    "15-1255.00",  # Web and Digital Interface Designers
    "15-1211.00",  # Computer Systems Analysts
    "15-1212.00",  # Information Security Analysts
    "15-1221.00",  # Computer and Information Research Scientists
    "15-1231.00",  # Computer Network Support Specialists
    "15-1232.00",  # Computer User Support Specialists
    "15-1241.00",  # Computer Network Architects
    "15-1242.00",  # Database Administrators
    "15-1243.00",  # Database Architects
    "15-1244.00",  # Network and Computer Systems Administrators
    "15-1299.08",  # Computer Systems Engineers/Architects
    "15-1299.09",  # Information Technology Project Managers
    "15-2051.00",  # Data Scientists
    "15-2041.00",  # Statisticians
    "11-3021.00",  # Computer and Information Systems Managers
    "17-2061.00",  # Computer Hardware Engineers
    "13-1082.00",  # Project Management Specialists
]

TOP_TRANSFERABLE = 8
TOP_ESSENTIAL = 5
TOP_KNOWLEDGE = 5

# O*NET flags technologies as hot / in-demand but publishes no 0-100 importance for them.
# These bands are ours, not O*NET's, and are disclosed in the dataset's importanceNotes.
# They sit below skill importance on purpose: an occupation's technology list holds many
# interchangeable alternatives that no single practitioner would ever use all of.
TECH_BAND = {"in-demand hot": 45, "hot": 35}


def get(url):
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=60) as resp:
        return resp.read().decode("utf-8", errors="replace")


def table_csv(kind, soc, name, sort="IM&t=-10"):
    slug = soc.replace(".", "-")
    url = f"{BASE}/link/table/details/{kind}/{soc}/{name}_{slug}.csv?fmt=csv&s={sort}"
    try:
        return list(csv.DictReader(io.StringIO(get(url))))
    except urllib.error.HTTPError as ex:
        # Not every occupation publishes every descriptor table.
        if ex.code == 404:
            sys.stderr.write(f"  no {name} table for {soc}\n")
            return []
        raise


def importance_rows(rows, label, kind, limit):
    out = []
    for row in rows[:limit]:
        name = (row.get(label) or "").strip()
        imp = (row.get("Importance") or "").strip()
        if not name or not imp:
            continue
        out.append({"name": name, "kind": kind, "importance": int(round(float(imp)))})
    return out


def technologies(page_html):
    """O*NET marks hot/in-demand technologies with a link carrying ?e=<technology name>."""
    found = {}
    pattern = re.compile(
        r'/search/tech/example\?e=([^&"]+)[^>]*?title="([^"]*?)Hot Technology"')
    for match in pattern.finditer(page_html):
        prefix = match.group(2).strip().lower()
        band = "in-demand hot" if prefix.startswith("in-demand") else "hot"
        name = html.unescape(urllib.parse.unquote(match.group(1))).strip()
        if not name:
            continue
        # Keep the strongest band seen for a technology.
        if TECH_BAND[band] > TECH_BAND.get(found.get(name, ""), 0):
            found[name] = band

    # Every technology O*NET flags for the occupation is kept - picking a "top N" out of a
    # flat list would be our editorial judgement rather than published data.
    ordered = sorted(found.items(), key=lambda kv: (-TECH_BAND[kv[1]], kv[0]))
    return [
        {"name": name, "kind": "Technology", "importance": TECH_BAND[band]}
        for name, band in ordered
    ]


def occupation(soc):
    page = get(f"{BASE}/link/details/{soc}")

    title_match = re.search(r"<title>\s*[\d.\-]+\s*-\s*([^<]+)</title>", page)
    title = html.unescape(title_match.group(1)).strip() if title_match else soc

    alt_match = re.search(
        r"<b>Sample of reported job titles:</b>\s*\n?([^<]+)", page
    )
    alternates = []
    if alt_match:
        for piece in html.unescape(alt_match.group(1)).split(","):
            piece = piece.strip()
            if not piece:
                continue
            alternates.append(piece)
            # "DevOps Engineer (Development Operations Engineer)" -> also index both forms.
            paren = re.match(r"^(.*?)\s*\((.+)\)$", piece)
            if paren:
                alternates.append(paren.group(1).strip())
                alternates.append(paren.group(2).strip())

    seen = set()
    alternates = [
        a for a in alternates
        if a and not (a.lower() in seen or seen.add(a.lower()))
    ]

    items = []
    items += importance_rows(
        table_csv("sc", soc, "Transferable_Skills"), "Transferable Skill", "Skill", TOP_TRANSFERABLE)
    items += importance_rows(
        table_csv("sb", soc, "Essential_Skills"), "Essential Skill", "Skill", TOP_ESSENTIAL)
    items += importance_rows(
        table_csv("kn", soc, "Knowledge"), "Knowledge", "Knowledge", TOP_KNOWLEDGE)

    items += technologies(page)

    return {
        "socCode": soc,
        "title": title,
        "alternateTitles": alternates,
        "items": items,
    }


def main():
    occupations = []
    for soc in SOCS:
        sys.stderr.write(f"fetching {soc}\n")
        occupations.append(occupation(soc))
        time.sleep(1.0)

    print(json.dumps({"occupations": occupations}, indent=2))


if __name__ == "__main__":
    main()
