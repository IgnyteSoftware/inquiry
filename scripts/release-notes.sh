#!/bin/sh
# Print the CHANGELOG.md section for one version, for use as the release body.
# Usage: scripts/release-notes.sh VERSION [CHANGELOG]
# VERSION can be a tag such as v1.4.0. The script fails, and prints nothing,
# when the version has no section, the section is empty, or the version is the
# newest release and Unreleased still has entries.
set -eu

if [ "$#" -lt 1 ] || [ "$#" -gt 2 ]; then
  echo "Usage: $0 VERSION [CHANGELOG]" >&2
  exit 2
fi

version=${1#v}
changelog=${2:-CHANGELOG.md}

if [ ! -f "$changelog" ]; then
  echo "$changelog does not exist." >&2
  exit 1
fi

awk -v version="$version" -v changelog="$changelog" '
  function is_link(line) {
    sub(/^(   |  | )/, "", line)
    return line ~ /^\[[^]]+\]:/
  }

  function link_label(line) {
    sub(/^(   |  | )/, "", line)
    return squeeze(tolower(substr(line, 2, index(line, "]:") - 2)))
  }

  # Markdown matches labels with runs of whitespace treated as one space.
  function squeeze(text) {
    gsub(/[ \t\n]+/, " ", text)
    return text
  }

  # Return the backticks or tildes that open or close a code fence, or "".
  # Four leading spaces make an indented code block, not a fence.
  function fence_run(line,   c, n) {
    sub(/^(   |  | )/, "", line)
    c = substr(line, 1, 1)
    if (c != "`" && c != "~") { return "" }
    n = 1
    while (substr(line, n + 1, 1) == c) { n++ }
    return n >= 3 ? substr(line, 1, n) : ""
  }

  function closes_fence(line,   run) {
    run = fence_run(line)
    if (run == "" || substr(run, 1, 1) != substr(fence, 1, 1) || length(run) < length(fence)) { return 0 }
    sub(/^(   |  | )/, "", line)
    return substr(line, length(run) + 1) ~ /^[ \t]*$/
  }

  function hold(line) { held[++holding] = line }

  # Remember the first definition of each label anywhere in the file. A
  # section can use a definition that sits in another section or the footer.
  function define(line,   label) {
    label = link_label(line)
    if (label in defs) { return "" }
    defs[label] = line
    order[++defined] = label
    return label
  }

  # Keep held lines that sit between entries. Before the first entry, keep
  # only link definitions, so the body does not start with blank lines.
  function flush(   i) {
    for (i = 1; i <= holding; i++) {
      if (count > 0 || is_link(held[i])) { lines[++count] = held[i] }
    }
    holding = 0
  }

  # At the end of a section, keep its link definitions. At the end of the
  # file, also drop the version links, because they are the changelog footer.
  function keep_links(at_end,   i, last) {
    last = 0
    for (i = 1; i <= holding; i++) {
      if (is_link(held[i]) && !(at_end && (link_label(held[i]) in headings))) { last = i }
    }
    for (i = 1; i <= last; i++) {
      if (!(at_end && is_link(held[i]) && (link_label(held[i]) in headings))) { lines[++count] = held[i] }
    }
    holding = 0
  }

  NR == 1 { sub(/^\357\273\277/, "") }

  {
    sub(/\r$/, "")
    prev_def = last_def
    last_def = ""
    prev_bare = bare
    bare = ""
    code = 0
    url_line = 0
    # Fenced text is content even when it looks like a heading. Comment text
    # is never content.
    if (fence != "") {
      if (closes_fence($0)) { fence = "" }
      kind = "content"
      code = 1
    } else if (comment) {
      if (index($0, "-->")) { comment = 0 }
      kind = "filler"
    } else if (fence_run($0) != "") {
      fence = fence_run($0)
      kind = "content"
      code = 1
    } else if ($0 ~ /^(   |  | )?<!--/) {
      if (!index($0, "-->")) { comment = 1 }
      kind = "filler"
    } else if ($0 ~ /^## /) {
      kind = "heading"
    } else if ($0 ~ /^#/) {
      kind = "subheading"
    } else if ($0 ~ /^[ \t]*$/ || is_link($0)) {
      kind = "filler"
      # A definition can put its URL on the next line.
      if ($0 ~ /\]:[ \t]*[^ \t]/) { last_def = define($0) } else if (is_link($0)) { bare = $0 }
    } else if (prev_bare != "" && $0 ~ /^[ \t]+(<[^>]*>|[^ \t<][^ \t]*)([ \t]+("[^"]*"|\047[^\047]*\047|\([^)]*\)))?[ \t]*$/) {
      # An indented URL after "[label]:" belongs to that definition, like a title.
      kind = "title"
      last_def = define(prev_bare "\n" $0)
      url_line = 1
    } else if (after_link && $0 ~ /^[ \t]+("[^"]*"|\047[^\047]*\047|\([^)]*\))[ \t]*$/) {
      # An indented quoted line after a link definition is its title.
      kind = "title"
      if (prev_def != "") { defs[prev_def] = defs[prev_def] "\n" $0 }
    } else {
      kind = "content"
    }
    after_link = is_link($0) || url_line
  }

  kind == "heading" {
    if (section == "notes") { keep_links(0) }
    name = ""
    # "## [1.2.0]-hotfix" is not a version heading.
    if ($0 ~ /^## \[/ && $0 !~ /^## \[[^]]+\][-A-Za-z0-9._+]/) {
      name = substr($0, 5)
      name = substr(name, 1, index(name, "]") - 1)
      headings[tolower(name)] = 1
    }
    if (tolower(name) == "unreleased") {
      section = "unreleased"
    } else if (name != "" && (name == version || name == "v" version)) {
      found = 1
      newest = (releases == 0)
      section = "notes"
      releases++
    } else {
      section = "other"
      if (name != "") { releases++ }
    }
    next
  }

  section == "unreleased" && kind == "content" { pending = 1 }

  section == "notes" {
    if (kind == "title" && holding > 0) {
      held[holding] = held[holding] "\n" $0
      next
    }
    if (kind == "filler" || kind == "title") {
      hold($0)
      next
    }
    if (kind == "content") { entries = 1 }
    flush()
    lines[++count] = $0
    if (code) { fenced[count] = 1 }
  }

  END {
    if (section == "notes") { keep_links(1) }
    if (!found) {
      printf "%s has no \"## [%s]\" section. Cut the changelog before you tag.\n", changelog, version > "/dev/stderr"
      exit 1
    }
    if (!entries) {
      printf "The \"## [%s]\" section in %s has no entries.\n", version, changelog > "/dev/stderr"
      exit 1
    }
    if (newest && pending) {
      printf "Unreleased in %s still has entries. Move them into \"## [%s]\" before you tag.\n", changelog, version > "/dev/stderr"
      exit 1
    }
    # Add the definitions that the section uses but does not contain.
    for (i = 1; i <= count; i++) {
      # Brackets in fenced code are not links.
      if (i in fenced) { continue }
      if (is_link(lines[i])) { have[link_label(lines[i])] = 1 }
      text = text " " tolower(lines[i])
    }
    text = squeeze(text)
    for (i = 1; i <= defined; i++) {
      label = order[i]
      if (!(label in have) && index(text, "[" label "]")) {
        if (!added++) { lines[++count] = "" }
        lines[++count] = defs[label]
      }
    }
    for (i = 1; i <= count; i++) { print lines[i] }
  }
' "$changelog"
