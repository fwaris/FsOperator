module Scripts
    
let drawCircle (x:int) (y:int) (radius:int) (opacity:float) (duration:int) = $"""
{{
const circle = document.createElement('div');
circle.style.position = 'absolute';
circle.style.width = '50px';
circle.style.height = '50px';
circle.style.backgroundColor = 'rgba(254, 153, 0, {opacity})';
circle.style.borderRadius = '50%%';
circle.style.pointerEvents = 'none';
circle.style.left = `${{{x} - 25}}px`; // Center it correctly
circle.style.top = `${{{y} - 25}}px`;
circle.style.zIndex = '2147483647'; // Highest possible z-index
document.body.appendChild(circle);
setTimeout(() => circle.remove(), {duration});
return 'done'
}}
"""

let drawArrow = """
(function(uniqueId, x, y, length, angleDeg, duration = 2000, opacity = 1.0) {
  const svgNS = "http://www.w3.org/2000/svg";

  const angleRad = angleDeg * Math.PI / 180;
  const x2 = x + length * Math.cos(angleRad);
  const y2 = y + length * Math.sin(angleRad);

  const svg = document.createElementNS(svgNS, "svg");
  svg.setAttribute("style", `position:absolute; top:0; left:0; width:100%; height:100%; pointer-events:none; opacity:${opacity};`);
  svg.style.zIndex = 9999;

  const defs = document.createElementNS(svgNS, "defs");
  const marker = document.createElementNS(svgNS, "marker");
  const markerId = uniqueId + "-arrowhead";

  marker.setAttribute("id", markerId);
  marker.setAttribute("markerWidth", "10");
  marker.setAttribute("markerHeight", "7");
  marker.setAttribute("refX", "10");
  marker.setAttribute("refY", "3.5");
  marker.setAttribute("orient", "auto");
  marker.setAttribute("markerUnits", "strokeWidth");

  const arrowPath = document.createElementNS(svgNS, "path");
  arrowPath.setAttribute("d", "M0,0 L10,3.5 L0,7 Z");
  arrowPath.setAttribute("fill", "black");

  marker.appendChild(arrowPath);
  defs.appendChild(marker);
  svg.appendChild(defs);

  const arrow = document.createElementNS(svgNS, "line");
  arrow.setAttribute("x1", x);
  arrow.setAttribute("y1", y);
  arrow.setAttribute("x2", x2);
  arrow.setAttribute("y2", y2);
  arrow.setAttribute("stroke", "black");
  arrow.setAttribute("stroke-width", "2");
  arrow.setAttribute("marker-end", `url(#${markerId})`);
  arrow.setAttribute("id", uniqueId + "-line");

  const circleRadius = 4;

  const startCircle = document.createElementNS(svgNS, "circle");
  startCircle.setAttribute("cx", x);
  startCircle.setAttribute("cy", y);
  startCircle.setAttribute("r", circleRadius);
  startCircle.setAttribute("fill", "black");
  startCircle.setAttribute("id", uniqueId + "-startCircle");

  const endCircle = document.createElementNS(svgNS, "circle");
  endCircle.setAttribute("cx", x2);
  endCircle.setAttribute("cy", y2);
  endCircle.setAttribute("r", circleRadius);
  endCircle.setAttribute("fill", "black");
  endCircle.setAttribute("id", uniqueId + "-endCircle");

  svg.appendChild(arrow);
  svg.appendChild(startCircle);
  svg.appendChild(endCircle);

  document.body.appendChild(svg);
  setTimeout(function() {
    document.body.removeChild(svg);
  }, duration);
})
"""


let drawDragArrow = """
(function(x1, y1, x2, y2, duration) {
  (function(x1, y1, x2, y2, duration) { 
  const svgNS = "http://www.w3.org/2000/svg";

  // Create a root wrapper div
  const root = document.createElement("div");
  root.setAttribute("style", "position:absolute; top:0; left:0; width:100%; height:100%; pointer-events:none; z-index:9999;");

  // Create the SVG element
  const svg = document.createElementNS(svgNS, "svg");
  svg.setAttribute("width", "100%");
  svg.setAttribute("height", "100%");
  svg.setAttribute("style", "width:100%; height:100%;");

  // Define arrow marker
  const defs = document.createElementNS(svgNS, "defs");
  const marker = document.createElementNS(svgNS, "marker");
  marker.setAttribute("id", "arrowhead");
  marker.setAttribute("markerWidth", "10");
  marker.setAttribute("markerHeight", "7");
  marker.setAttribute("refX", "10");
  marker.setAttribute("refY", "3.5");
  marker.setAttribute("orient", "auto");
  marker.setAttribute("markerUnits", "strokeWidth");

  const arrowPath = document.createElementNS(svgNS, "path");
  arrowPath.setAttribute("d", "M0,0 L10,3.5 L0,7 Z");
  arrowPath.setAttribute("fill", "black");

  marker.appendChild(arrowPath);
  defs.appendChild(marker);
  svg.appendChild(defs);

  // Create arrow line
  const arrow = document.createElementNS(svgNS, "line");
  arrow.setAttribute("x1", x1);
  arrow.setAttribute("y1", y1);
  arrow.setAttribute("x2", x2);
  arrow.setAttribute("y2", y2);
  arrow.setAttribute("stroke", "blue");
  arrow.setAttribute("stroke-width", "5");
  arrow.setAttribute("marker-end", "url(#arrowhead)");

  // Create start and end circles
  const circleRadius = 4;

  const startCircle = document.createElementNS(svgNS, "circle");
  startCircle.setAttribute("cx", x1);
  startCircle.setAttribute("cy", y1);
  startCircle.setAttribute("r", circleRadius);
  startCircle.setAttribute("fill", "black");

  const endCircle = document.createElementNS(svgNS, "circle");
  endCircle.setAttribute("cx", x2);
  endCircle.setAttribute("cy", y2);
  endCircle.setAttribute("r", circleRadius);
  endCircle.setAttribute("fill", "black");

  // Assemble SVG
  svg.appendChild(arrow);
  svg.appendChild(startCircle);
  svg.appendChild(endCircle);
  root.appendChild(svg);

  // Append to DOM in one go
  document.body.appendChild(root);

  // Remove after specified duration
  setTimeout(() => {
    document.body.removeChild(root);
  }, duration);
})

"""