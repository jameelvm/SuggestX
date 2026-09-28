import type { ReactNode } from "react";

import type { TrieTreeNode } from "@/types/insights";

interface LayoutNode {
  node: TrieTreeNode;
  x: number;
  y: number;
  children: LayoutNode[];
}

const X_SPACING = 130;
const Y_SPACING = 70;

/**
 * Standard small-tree layout: a leaf gets the next free horizontal slot,
 * an internal node sits at the midpoint of its children — computed once
 * per render, cheap at this project's demo scale (a few dozen nodes at
 * most, the same scale `CompressedTrie.ToSnapshot` itself only expects).
 */
function layout(node: TrieTreeNode, depth: number, cursor: { x: number }): LayoutNode {
  if (node.children.length === 0) {
    const x = cursor.x * X_SPACING;
    cursor.x += 1;
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
      strokeWidth={1.5}
    />,
    <text
      key={`label-${child.x}-${child.y}`}
      x={(laidOut.x + child.x) / 2}
      y={(laidOut.y + child.y) / 2 - 4}
      fontSize={11}
      textAnchor="middle"
      fill="#737373"
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
      <circle cx={x} cy={y} r={node.isTerminal ? 7 : 5} fill={node.isTerminal ? "#059669" : "#a3a3a3"} />
      {node.isTerminal && (
        <text x={x} y={y + 20} fontSize={11} textAnchor="middle" fill="#171717">
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
 */
export function TrieGraph({ root }: { root: TrieTreeNode }) {
  const laidOut = layout(root, 0, { x: 0 });
  const bounds = { maxX: 0, maxY: 0 };
  collectBounds(laidOut, bounds);

  // Terminal labels are centered text that can extend well past their
  // node's own x position (a long phrase like "python programming (1)"
  // is wider than the X_SPACING between nodes) — padding wide enough for
  // that, not just for the node positions themselves, is what keeps the
  // leftmost/rightmost labels from clipping against the viewBox edge.
  const labelPadding = 90;
  const width = bounds.maxX + X_SPACING;
  const height = bounds.maxY + Y_SPACING;

  return (
    <svg
      viewBox={`${-labelPadding} -20 ${width + labelPadding * 2} ${height + 40}`}
      className="w-full"
      style={{ maxHeight: 420 }}
    >
      {renderEdges(laidOut)}
      {renderNodes(laidOut)}
    </svg>
  );
}
