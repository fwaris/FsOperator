namespace FsOperator

module Scripts =
    let INDICATOR_TIME_MS = 1000

    let drawArrowFunction = """
function (x, y, length, angle, duration = 2000) {
    // Create a new canvas element
    const canvas = document.createElement('canvas');
    canvas.width = 400;
    canvas.height = 300;
    canvas.style.position = 'fixed';
    canvas.style.left = `${x - canvas.width / 2}px`; // Centering logic
    canvas.style.top = `${y - canvas.height / 2}px`;

    // Append canvas to the body
    document.body.appendChild(canvas);
    const ctx = canvas.getContext('2d');

// Function to draw fat arrow with base at (x, y)
function drawFatArrow(ctx, x, y, length, angle, arrowHeadLength = 15, lineWidth = 4, color = 'orange') {
    const endX = x + length * Math.cos(angle);
    const endY = y + length * Math.sin(angle);

    // Draw the thick arrow shaft
    ctx.beginPath();
    ctx.moveTo(x, y);  // Start at base
    ctx.lineTo(endX, endY);  // End at arrow tip
    ctx.lineWidth = lineWidth;
    ctx.strokeStyle = color;
    ctx.lineCap = 'round';
    ctx.stroke();

    // Arrowhead points
    const headAngle1 = angle + Math.PI / 6;
    const headAngle2 = angle - Math.PI / 6;
    const arrowPoint1X = endX - arrowHeadLength * Math.cos(headAngle1);
    const arrowPoint1Y = endY - arrowHeadLength * Math.sin(headAngle1);
    const arrowPoint2X = endX - arrowHeadLength * Math.cos(headAngle2);
    const arrowPoint2Y = endY - arrowHeadLength * Math.sin(headAngle2);

    // Draw the arrowhead
    ctx.beginPath();
    ctx.moveTo(endX, endY);
    ctx.lineTo(arrowPoint1X, arrowPoint1Y);
    ctx.lineTo(arrowPoint2X, arrowPoint2Y);
    ctx.lineTo(endX, endY);
    ctx.fillStyle = color;
    ctx.fill();
}


    // Draw the arrow at the center of the canvas
    drawFatArrow(ctx, canvas.width / 2, canvas.height / 2, length, angle);

    // Set timeout to remove the canvas after the given duration
    setTimeout(() => {
        document.body.removeChild(canvas);
    }, duration);
}

"""
    
    let drawClickFunction = """
function drawCircle(x, y, duration = 2000) {
    const circle = document.createElement('div');
    circle.style.position = 'fixed';
    circle.style.width = '50px';
    circle.style.height = '50px';
    circle.style.backgroundColor = 'rgba(254, 153, 0, 0.70)';
    circle.style.borderRadius = '50%';
    circle.style.pointerEvents = 'none';
    circle.style.left = `${x - 25}px`; // Center it correctly
    circle.style.top = `${y - 25}px`;
    circle.style.zIndex = '2147483647'; // Highest possible z-index
    document.body.appendChild(circle);
    setTimeout(() => circle.remove(), duration);
}
"""

    let indicatorScript_global = $"""
if (!window.__indicatorInjected) {{
    window.__indicatorInjected = true;
    window.drawClick = {drawClickFunction}
    window.drawArrow = {drawArrowFunction}
    console.log('✅ Click indicator script injected');
}};
"""

    let indicatorScript_page = $"""
() => {{
    window.drawClick = {drawClickFunction}
    window.drawArrow = {drawArrowFunction}
}};
"""
