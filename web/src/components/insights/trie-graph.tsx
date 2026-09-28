import type { ReactNode } from "react";

import type { TrieTreeNode } from "@/types/insights";

interface LayoutNode {
  node: TrieTreeNode;
  x: number;
  y: number;
  children: LayoutNode[];
}

const Y_SPACING = 100;
const LABEL_FONT_SIZE = 14;
const NODE_RADIUS = { internal: 6, terminal: 9 };
const MIN_LEAF_GAP = 28;
const MIN_LEAF_ADVANCE = 90;
const SIDE_PADDING = 20;

// A rough average glyph width at LABEL_FONT_SIZE, not an exact text
// measurement (that needs a real DOM/canvas call this layout — run
// during render, before anything is on screen to measure — can't make).
// Good enough to keep two adjacent long phrases from visually
// overlapping, which a fixed per-leaf spacing could never guarantee once
// phrase lengths vary as much as this project's own test data does
// ("solo" vs. "mykonos summer perfume").
const CHAR_WIDTH = 7.6;

function estimateLabelWidth(node: TrieTreeNode): number {
  if (!node.isTerminal) return 0;
  return `${node.phrase} (${node.frequency})`.length * CHAR_WIDTH;
}

/**
 * Standard small-tree layout, with one deliberate departure from the
 * textbook version: a leaf's horizontal slot is sized by its own label
 * width (plus a fixed gap), not a uniform spacing constant — so "solo"
 * and "mykonos summer perfume" each get exactly the room their own
 * rendered text needs, not the same fixed box. An internal node still
 * sits at the midpoint of its children, computed once per render, cheap
 * at this project's demo scale.
 */
function layout(node: TrieTreeNode, depth: number, cursor: { x: number }): LayoutNode {
  if (node.children.length === 0) {
    const halfWidth = Math.max(estimateLabelWidth(node) / 2, MIN_LEAF_ADVANCE / 2);
    const x = cursor.x + halfWidth;
    cursor.x = x + halfWidth + MIN_LEAF_GAP;
    return { node, x, y: depth * Y_SPACING, children: [] };
  }

  const children = node.children.map((child) => layout(child, depth + 1, cursor));
  const x = (children[0].x + children[children.length - 1].x) / 2;
  return { node, x, y: depth * Y_SPACING, children };
}

function collectBounds(laidOut: LayoutNode, bounds: { maxX: number; maxY: number }) {
  bounds.maxX = Math.max(bounds.maxX, laidOut.x);
  bounds.maxY = Math.max(bounds.maxY, laidOut.y);
  laidOut.children.forEach((child) => collectBounds(child, bounds));
}

// Segments label the edge into a node, not the node itself — a compressed
// trie's segment is what's consumed *to reach* that node, matching
// DESIGN.md's own framing of the data structure.
function renderEdges(laidOut: LayoutNode): ReactNode[] {
  return laidOut.children.flatMap((child) => [
    <line
      key={`edge-${child.x}-${child.y}`}
      x1={laidOut.x}
      y1={laidOut.y}
      x2={child.x}
      y2={child.y}
      stroke="#d4d4d4"
      strokeWidth={2}
    />,
    <text
      key={`label-${child.x}-${child.y}`}
      x={(laidOut.x + child.x) / 2}
      y={(laidOut.y + child.y) / 2 - 8}
      fontSize={LABEL_FONT_SIZE}
      textAnchor="middle"
      fill="#525252"
    >
      {child.node.segment}
    </text>,
    ...renderEdges(child),
  ]);
}

function renderNodes(laidOut: LayoutNode): ReactNode[] {
  const { node, x, y } = laidOut;
  return [
    <g key={`node-${x}-${y}`}>
      <circle
        cx={x}
        cy={y}
        r={node.isTerminal ? NODE_RADIUS.terminal : NODE_RADIUS.internal}
        fill={node.isTerminal ? "#059669" : "#a3a3a3"}
      />
      {node.isTerminal && (
        <text x={x} y={y + 26} fontSize={LABEL_FONT_SIZE} fontWeight={500} textAnchor="middle" fill="#171717">
          {node.phrase} ({node.frequency})
        </text>
      )}
    </g>,
    ...laidOut.children.flatMap(renderNodes),
  ];
}

/**
 * A real graph of TrieBuilder's own in-memory structure, not a mockup —
 * fetched fresh from `GET /_debug/tree` on every poll. Hand-rolled layout
 * rather than a charting library: this project's trie never has more than
 * a few dozen nodes at demo scale, and pulling in a real dependency for
 * that would be exactly the unnecessary abstraction this project's own
 * conventions warn against.
 * <para>
 * Rendered at its natural, fixed pixel size — never scaled down to fit a
 * narrow container — wrapped in a horizontally scrolling strip instead.
 * An earlier version used a `viewBox` + `w-full` SVG, which shrank every
 * label proportionally on anything less than a very wide screen; that,
 * combined with fixed-width leaf slots too narrow for this project's own
 * longer phrases, is what made it hard to read — not a font-size choice
 * on its own.
 * </para>
 */
export function TrieGraph({ root }: { root: TrieTreeNode }) {
  const laidOut = layout(root, 0, { x: SIDE_PADDING });
  const bounds = { maxX: 0, maxY: 0 };
  collectBounds(laidOut, bounds);

  const topPadding = 30;
  const width = bounds.maxX + MIN_LEAF_ADVANCE / 2 + SIDE_PADDING;
  const height = bounds.maxY + 80;

  return (
    <div className="overflow-x-auto">
      <svg width={width} height={height + topPadding} className="block">
        <g transform={`translate(0, ${topPadding})`}>
          {renderEdges(laidOut)}
          {renderNodes(laidOut)}
        </g>
      </svg>
    </div>
  );
}
