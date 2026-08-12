"""Assemble the shipped benchmark dataset from the raw O*NET extract.

Adds the metadata block, the evidence-term map used to spot supporting bullets, and
derived short forms for technology names so "Amazon Web Services AWS software" can be
matched by a bullet that just says "AWS".
"""

import json
import re
import sys

RAW = "raw-occupations.json"

# Bullet-text keywords that count as evidence for an O*NET descriptor. The descriptor names
# are standardised across occupations, so one map covers the whole dataset. Terms are matched
# as case-insensitive substrings, so each one is chosen to avoid matching unrelated words
# ("testing" rather than "test", which is a substring of "latest").
EVIDENCE_TERMS = {
    "Active Learning": ["self-taught", "upskill", "certification", "training", "new technology", "adopted", "learned"],
    "Active Listening": ["stakeholder", "gathered requirements", "user research", "interview", "feedback"],
    "Administration and Management": ["managed", "budget", "roadmap", "strategy", "resourcing", "operations"],
    "Communications and Media": ["documentation", "published", "content", "blog", "newsletter", "wrote"],
    "Complex Problem Solving": ["root cause", "solved", "resolved", "debugg", "diagnos", "troubleshoot"],
    "Computers and Electronics": ["software", "hardware", "system", "application", "server", "network", "comput"],
    "Coordination": ["coordinat", "cross-functional", "collaborat", "partnered", "aligned"],
    "Critical Thinking": ["analyz", "analys", "evaluat", "assess", "trade-off", "tradeoff"],
    "Customer and Personal Service": ["customer", "client", "end user", "support ticket", "help desk", "service level"],
    "Design": ["design", "prototype", "wireframe", "user experience", "user interface", "mockup"],
    "Education and Training": ["trained", "training", "mentor", "onboard", "workshop", "coach"],
    "Engineering and Technology": ["engineer", "architect", "built", "implement", "deploy"],
    "English Language": ["documentation", "wrote", "authored", "specification", "runbook", "report"],
    "Instructing": ["trained", "training", "mentor", "onboard", "workshop", "taught"],
    "Judgment and Decision Making": ["decision", "prioriti", "recommend", "selected", "trade-off", "tradeoff"],
    "Management of Personnel Resources": ["led a team", "led the team", "direct report", "hiring", "staffing", "supervis", "managed a team"],
    "Mathematics": ["algorithm", "statistic", "quantitat", "calculat", "model", "forecast"],
    "Monitoring": ["monitor", "dashboard", "alerting", "observability", "telemetry", "tracked"],
    "Negotiation": ["negotiat", "vendor", "contract", "consensus", "buy-in"],
    "Operations Analysis": ["requirement", "specification", "scoping", "discovery", "analysis"],
    "Operations Monitoring": ["monitor", "uptime", "alerting", "health check", "observability", "service level"],
    "Programming": ["develop", "program", "cod", "script", "implement", "refactor"],
    "Quality Control Analysis": ["testing", "tested", "unit test", "code review", "quality", "regression", "validat"],
    "Reading Comprehension": ["specification", "documentation", "requirement", "standard", "rfc"],
    "Service Orientation": ["customer", "client", "end user", "support", "help desk"],
    "Social Perceptiveness": ["stakeholder", "cross-functional", "collaborat", "mentor", "team"],
    "Speaking": ["communicat", "briefed", "demo", "spoke at", "conference", "presentation"],
    "Systems Analysis": ["system", "architect", "workflow", "process", "integration"],
    "Systems Evaluation": ["performance", "benchmark", "optimiz", "profil", "latency", "throughput"],
    "Technology Design": ["design", "architect", "prototype", "framework", "platform", "built"],
    "Telecommunications": ["network", "tcp", "vpn", "dns", "bandwidth", "routing", "firewall"],
    "Time Management": ["deadline", "on time", "sprint", "delivered", "schedule", "ahead of"],
    "Troubleshooting": ["troubleshoot", "debug", "diagnos", "root cause", "incident", "outage", "hotfix"],
    "Writing": ["documentation", "wrote", "authored", "specification", "runbook", "report"],
}

# The matcher works on case-insensitive substrings, which cannot tell the language "C" or "R"
# apart from an ordinary letter inside another word. Technology names this short are dropped.
MIN_TECHNOLOGY_NAME = 3

IN_DEMAND_HOT = 45
MIN_TECHNOLOGIES = 5

METADATA = {
    "source": "O*NET OnLine (https://www.onetonline.org)",
    "sourceVersion": "O*NET 30.3 Database",
    "retrievedOn": "2026-08-11",
    "attribution": (
        "This product uses information from O*NET OnLine by the U.S. Department of Labor, "
        "Employment and Training Administration (USDOL/ETA), used under the CC BY 4.0 license. "
        "AngryFoot has modified the data by selecting a subset of occupations and descriptors. "
        "USDOL/ETA has not approved, endorsed, or tested these modifications. "
        "O*NET(R) is a trademark of USDOL/ETA."
    ),
    "notes": [
        "Aggregate occupational data only. This dataset describes occupations, never individuals, "
        "and contains no information about any identifiable person or any specific employer's staff.",
        "Skill and knowledge importance values are O*NET's published 0-100 Importance ratings for "
        "the occupation, transcribed unchanged.",
        "O*NET publishes no importance rating for technologies. AngryFoot assigns 45 to technologies "
        "O*NET flags as In-Demand Hot and 35 to those flagged Hot. These bands are AngryFoot's, not "
        "O*NET's, and sit below skill importance because an occupation's technology list holds many "
        "interchangeable alternatives no single practitioner would use all of.",
        "Only technologies O*NET flags In-Demand Hot for the occupation are kept; where fewer than "
        "five carry that flag, Hot-flagged technologies top the list up to five. O*NET lists dozens "
        "to hundreds of technologies per occupation, and benchmarking against all of them would "
        "report an unrealistically poor coverage figure for any real practitioner.",
        "Technology names shorter than three characters (C, R, Go) are omitted: the substring matcher "
        "used to find supporting bullets cannot distinguish them from letters inside other words.",
        "A few newer occupations (Data Scientists, Project Management Specialists, Web and Digital "
        "Interface Designers) do not yet publish skill or knowledge ratings; those entries carry "
        "technology data only.",
        "evidenceTerms maps each descriptor to the bullet-text keywords that count as supporting "
        "evidence for it. Those keyword lists are AngryFoot's, not O*NET's: O*NET names the "
        "requirement, AngryFoot decides what in a resume bullet demonstrates it.",
    ],
}


def technology_terms(name):
    """Short forms a bullet is likely to actually use for a long O*NET technology name."""
    terms = [name]

    trimmed = re.sub(r"\s+software$", "", name, flags=re.IGNORECASE).strip()
    if trimmed and trimmed.lower() != name.lower():
        terms.append(trimmed)

    # "Amazon Web Services AWS software" -> "AWS"; "Extensible markup language XML" -> "XML".
    for token in re.findall(r"\b[A-Z][A-Z0-9]{2,5}\b", name):
        terms.append(token)

    seen = set()
    return [t for t in terms if not (t.lower() in seen or seen.add(t.lower()))]


def main():
    raw = json.load(open(RAW, encoding="utf-8"))

    occupations = []
    unmapped = set()
    evidence = {}

    for occ in raw["occupations"]:
        technologies = [
            t for t in occ["items"]
            if t["kind"] == "Technology" and len(t["name"]) >= MIN_TECHNOLOGY_NAME
        ]
        in_demand = [t for t in technologies if t["importance"] == IN_DEMAND_HOT]
        if len(in_demand) < MIN_TECHNOLOGIES:
            kept = technologies[:MIN_TECHNOLOGIES]
        else:
            kept = in_demand
        kept_names = {t["name"] for t in kept}

        items = []
        for item in occ["items"]:
            if item["kind"] == "Technology":
                if item["name"] not in kept_names:
                    continue
                terms = technology_terms(item["name"])
            else:
                extra = EVIDENCE_TERMS.get(item["name"])
                if extra is None:
                    unmapped.add(item["name"])
                    extra = []
                terms = [item["name"]] + extra

            # Terms are shared across occupations, so they live in one top-level map
            # rather than being repeated on every item that names the same descriptor.
            evidence.setdefault(item["name"], terms)
            items.append({
                "name": item["name"],
                "kind": item["kind"],
                "importance": item["importance"],
            })

        occupations.append({
            "socCode": occ["socCode"],
            "title": occ["title"],
            "alternateTitles": occ["alternateTitles"],
            "items": items,
        })

    if unmapped:
        sys.stderr.write(f"WARNING: no evidence terms for {sorted(unmapped)}\n")

    dataset = dict(METADATA)
    dataset["evidenceTerms"] = {name: evidence[name] for name in sorted(evidence)}
    dataset["occupations"] = occupations

    with open("onet-occupations.json", "w", encoding="utf-8", newline="\n") as handle:
        json.dump(dataset, handle, indent=2, ensure_ascii=True)
        handle.write("\n")

    total_items = sum(len(o["items"]) for o in occupations)
    sys.stderr.write(f"wrote {len(occupations)} occupations, {total_items} items\n")


if __name__ == "__main__":
    main()
