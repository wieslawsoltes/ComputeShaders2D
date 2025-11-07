# Technical Specification — Prefix‑Sum CPU + WebGPU Vector Renderer with Animation

> This document specifies the complete behavior of the renderer’s JavaScript code, data structures, algorithms, and WGSL shaders. It covers CPU and WebGPU code paths, the path‑building API, SVG/text ingestion, tiling & prefix‑sum binning, rasterization, SSAA, compositing, and the animation/FPS system.

---

## 0. Nomenclature & Coordinate System

* **Canvas space**: Pixel coordinates with origin at the **top‑left** of the canvas, +X to the right, +Y downward.
* **Path**: A sequence of subpaths, each built from segment commands `{M,L,Q,C,Arc,Ellipse,Close}`.
* **Shape**: A closed polygon intended for filling (fill rule: even‑odd or non‑zero).
* **Tile**: Axis‑aligned rectangular chunk of pixels of size `tileSize × tileSize` (last row/column may be partial).
* **SSAA**: Supersampling antialiasing. Each final pixel is computed by averaging `SS × SS` sub‑samples.
* **Premultiplied alpha**: Colors are composited in premultiplied form inside the rasterizer (`rgb := rgb * a`).

---

## 1. High‑Level Pipeline

1. **User Program Execution**
   Arbitrary user code (from the playground textarea) is executed per frame inside an async closure with a provided `api`. The user code calls `api.fillPath()` / `api.strokePath()` etc. to emit **shapes**.

2. **Geometry Preparation**

   * Path commands get **flattened** into polylines (curves → sequences of line segments).
   * Strokes are **expanded** into filled polygons (quads/fans implementing joins and caps).

3. **Tiling & Prefix‑Sum Binning**

   * For each shape, a bounding‑box determines the covered tile range.
   * Tile membership counts are accumulated; an **exclusive scan** (prefix‑sum) yields `tileOffsets`.
   * A flattened `tileShapeIndex` array stores, per tile, the indices of shapes overlapping it.

4. **Rasterization** (CPU or WebGPU):

   * **CPU (Workers)**: Per tile, scanline fill using even‑odd or non‑zero rule, with SSAA done by rendering into a supersampled float buffer then averaging down.
   * **WebGPU Compute**: One thread per pixel. For each SSAA sub‑sample, test containment against all shapes in the pixel’s tile; composite premultiplied color; average over sub‑samples in‑shader; write to an RGBA8 storage texture, then **blit** to the canvas.

5. **Presentation & Metrics**

   * Final pixels are presented on the selected canvas.
   * Build time, raster time, tile stats, FPS (EMA), and per‑frame time (ms) are updated.

6. **Animation Loop**

   * `requestAnimationFrame` drives time (`api.time()`), timestep (`api.dt()`), and frame number (`api.frame()`).
   * Rendering is **re‑entrant safe** via `renderInFlight` gating.

---

## 2. Public Path‑Builder API

### 2.1 Geometry Construction

```ts
// Create a new path with internal transform (applied in flatten()).
api.path(): Path

// Path methods (chainable)
Path.moveTo(x,y)
Path.lineTo(x,y)
Path.quadTo(cpx,cpy, x,y)                 // Quadratic Bezier
Path.bezierTo(c1x,c1y, c2x,c2y, x,y)      // Cubic Bezier
Path.arc(cx,cy, r, a0,a1, ccw=false, segments?)
Path.rect(x,y,w,h, rx=0)
Path.ellipse(cx,cy, rx,ry, rot=0, segments=64)
Path.poly([[x,y], ...])
Path.transform({ translateX,translateY, scaleX,scaleY, rotate })
Path.closePath()
```

**Transform semantics**
Each path has an accumulated transform `T` (`tx, ty, sx, sy, rot`) that is **applied during `flatten()`** to every emitted vertex.

### 2.2 Emitting Shapes

```ts
// Fill a (flattenable) path; emits shapes with chosen fill rule ('evenodd' or 'nonzero').
api.fillPath(path, rgba, rule?)

// Expand stroke into polygons (joins/caps/miter) and emit those polygons (always even-odd).
api.strokePath(path, width, rgba, { join, cap, miterLimit })
```

**Color representation**: `rgba` is `Uint8ClampedArray [r,g,b,a]`, 0..255.

### 2.3 Text & SVG

```ts
const font = await api.loadFont(ttfUrlOrBuffer) // opentype.js if available, else stroke font fallback
api.fillText(font, text, x, y, size, rgba, { align })  // outlines → fill shapes
api.strokeText(font, text, x, y, size, width, rgba, { join, cap, miterLimit })

// SVG
const p = api.svgPath(d)           // Parse 'd' into Path
api.fillSVG(d, rgba, rule?)        // fillPath(parse(d))
api.strokeSVG(d, width, rgba, ...) // strokePath(parse(d))
```

### 2.4 Helpers & Timeline

```ts
api.color(r,g,b,a)
api.width(), api.height()
api.rule()                         // current UI fill rule toggle
api.param(name)                    // 'strokeWidth'|'join'|'cap'|'miterLimit'
api.star(cx,cy, rOuter,rInner, n)
api.randomPolyline(n)

// Animation:
api.time(): number   // seconds since play() / reset
api.dt(): number     // seconds between frames
api.frame(): number  // monotonically increasing frame index
```

---

## 3. Internal Geometry Representation

Each emitted **shape** is:

```ts
interface Shape {
  verts: Float32Array // length = 2*N (x0,y0,x1,y1,...)
  color: Uint8ClampedArray // [r,g,b,a], 0..255
  rule: 'evenodd' | 'nonzero'
}
```

**Closure invariant**: Fillable shapes must be **closed** (the flattening step ensures the last `(x,y)` equals the first `(x,y)` after `closePath()`).

---

## 4. Flattening Algorithms

Flattening converts curves/ellipses/arcs to polylines (list of `(x,y)` vertices).

### 4.1 Transform Application

Given path‑space vertex `(x,y)` and transform `T = (sx,sy,rot,tx,ty)`:

```
xr = x*sx
yr = y*sy
X  = xr*cos(rot) - yr*sin(rot) + tx
Y  = xr*sin(rot) + yr*cos(rot) + ty
```

### 4.2 Quadratic Bezier (adaptive subdivision)

* Input: `(x0,y0)` → control `(cx,cy)` → `(x1,y1)`.
* Error metric: distance between midpoint of curve and midpoint of chord.
* Subdivide recursively until `err ≤ tol` or depth > 10.

Pseudo:

```txt
stack ← [x0,y0,cx,cy,x1,y1, depth=0]
while stack not empty:
  pop segment
  mx,my = (x0 + 2*cx + x1)/4, (y0 + 2*cy + y1)/4
  lx,ly = (x0 + x1)/2,        (y0 + y1)/2
  err   = ||(mx,my)-(lx,ly)||
  if err <= tol or depth > 10:
    emit (x1,y1)
  else:
    q01=(x0+cx)/2,  q12=(cx+x1)/2
    h =(q01+q12)/2
    push right half [h, q12, x1], depth+1
    push left  half [x0, q01, h], depth+1
```

### 4.3 Cubic Bezier (adaptive subdivision)

* Input: `(x0,y0)` → `(c1x,c1y)` → `(c2x,c2y)` → `(x1,y1)`.
* Error metric: `|cross(c1 - x1, d)| + |cross(c2 - x1, d)|` where `d = (x1-x0, y1-y0)`.
  Accept if `(d1 + d2) ≤ tol*6` or depth > 10.

### 4.4 Circular Arcs & Ellipses

* `arc(cx,cy,r,a0,a1,ccw,segments)` samples `n = min(segments || ceil(|Δθ| / 18°), 64)` points.
* `ellipse(cx,cy,rx,ry,rot,segments)` samples `steps ∈ [8,256]`, then rotates by `rot`.

### 4.5 SVG Elliptical Arc `A` Command

Implements the SVG spec construction:

1. Transform endpoints into ellipse frame by `φ` (x‑axis rotation).
2. Compute center `(cx,cy)` that satisfies large‑arc and sweep flags.
3. Derive start angle `θ` and sweep `Δθ`.
4. Tesselate with step size ≈ 15°.
5. Transform back to canvas space.

---

## 5. Stroke Expansion (`strokeToPolys`)

Given polyline vertices `P[i]`, width `w`, half‑width `h = w/2`, join/cap/miter limit:

1. **Segment Quads**: For each segment `P[i]→P[i+1]`:

   * Tangent `t = (dx,dy)/||dx,dy||`.
   * Normal  `n = (-dy,dx)/||dx,dy||`.
   * Quad = `(P - n*h)` → `(P + n*h)` at both ends.

2. **Caps** (for open polylines):

   * **round**: semicircle fan from angle `θ - 90° → θ + 90°`, centered at endpoint.
   * **square**: extend endpoint by `t*h` and build a rectangle.
   * **butt**: nothing extra (implicit by segment quads).

3. **Joins**:

   * Orient via cross product `cross(v0, v1)`.
     `ccw = cross > 0`. Compute “outward” directions on the angle’s exterior.
   * **round**: arc fan between adjacent outward edge normals.
   * **bevel**: triangle fan `(corner, edge0_out, edge1_out)`.
   * **miter**:

     * Compute intersection of the two offset lines.
     * Miter length `mLen = ||intersection - corner|| / h`.
     * If `mLen ≤ miterLimit` use the intersection; else **fallback to bevel**.

All stroke output is emitted as **filled polygons** rendered with even‑odd rule.

---

## 6. Text Conversion

* If `opentype.js` loads:

  * `font.getPath(text, x, y, size)` returns glyph contours.
  * Convert `M/L/Q/C/Z` commands to `Path` and **close** open contours.
  * **Fill** text by flattening contours and pushing as shapes.
* Fallback (stroke font):

  * Each glyph is a polyline (in a 6×10 unit grid) stroked with `width = max(1, size*0.12)`, `join='round'`, `cap='round'`.
  * The stroke expansion algorithm converts to polygons.

---

## 7. Tiling & Prefix‑Sum Binning

### 7.1 Tile Layout

* Tiles grid: `tilesX = ceil(W/tileSize)`, `tilesY = ceil(H/tileSize)`.
* Tile index: `t = ty * tilesX + tx`.

### 7.2 Shape → Tiles

For each `shape s` with vertex array `V`:

1. Compute bounding box:
   `minX = min(V[0], V[2], ...)`, `maxX = max(...)`, same for Y.
2. Convert to tile range:
   `minTx = clamp(floor(minX / tileSize), 0, tilesX-1)`
   `maxTx = clamp(floor(maxX / tileSize), 0, tilesX-1)`
   (same for Ty).
3. Increment `counts[t]` for all tiles in the range.

### 7.3 Exclusive Scan

* `offsets = exclusiveScan(counts)` returns starting offsets into a flat index buffer.
* Total list length `L = sum(counts)`.

### 7.4 Scatter Indices

* `tileShapeIndex[offsets[t] + k] = shapeIndex` for each shape covering tile `t`, preserving **global shape order**.

**Data produced**:

* `counts: Uint32Array[tileCount]`
* `offsets: Uint32Array[tileCount]`
* `tileShapeIndex: Uint32Array[L]`

---

## 8. CPU Rasterization (Workers)

### 8.1 Worker Pool

* **Persistent** workers (`ensureWorkerPool(n)`) are created once; reused every frame.
* Work assignment: round‑robin distribute tile IDs across `n` buckets.

### 8.2 Tile Rendering (per worker)

Inputs: `tileSize`, `canvasW/H`, `tilesX`, `offsets`, `counts`, `tileShapeIndex`, `shapes`, `SS`.

1. Allocate float buffer `fbuf = Float32Array((tileW*SS) * (tileH*SS) * 4)`.

2. For each shape index in the tile:

   * `fillPolygonTile(tile)`:

     * Build `vx[], vy[]` with supersample scaling (`* SS`).
     * For each supersampled row `ry = 0..(sH-1)`:

       * World Y of sample center: `gy = sTileY + ry + 0.5`.
       * **Even‑Odd rule** (half‑open edge selection):

         * For each edge `(x0,y0)→(x1,y1)` where `(y0,y1)` straddles `gy` with a half‑open test `gy <= max(y0,y1) && gy > min(y0,y1)`, compute intersection `x`.
         * Sort intersections `xs[]`.
           Fill horizontal spans in pairs `[xs[0], xs[1]]`, `[xs[2], xs[3]]`, etc.
       * **Non‑Zero rule**:

         * Build events `E = { x, w }` where `w = +1` if crossing upward (`y1>y0`), else `-1`.
         * Sort by `x`. Sweep accumulating winding number `W`.
           Draw span when `W` transitions `0 → !=0`, end when `!=0 → 0`.
       * **Compositing**: For each filled pixel column:

         * Compute premultiplied source `sr,sg,sb,sa = (rgba/255) * a`.
         * Composite: `dst = src + (1 - src.a) * dst`.

3. **SSAA downsample**:

   * If `SS == 1`: Convert premultiplied floats to unpremultiplied `Uint8ClampedArray` directly:

     ```
     A = clamp(a,0,1)
     rgb = (A > ε) ? (rgb / A) : 0
     out = round(255 * [rgb, A])
     ```
   * Else for each base pixel, average `SS*SS` samples in premultiplied space, then unpremultiply.

4. Transfer `ImageData(pixels)` back to the main thread (transfer the `ArrayBuffer`).

### 8.3 Main Thread Composition

* `putImageData(img, tileX, tileY)` per tile onto the CPU `<canvas>`.
* **Timing**: `tRaster = now() - tR0`.

---

## 9. WebGPU Rasterization

### 9.1 Device & Surface

* Canvas context: `'webgpu'`.
* `alphaMode: 'premultiplied'`.
* Preferred swapchain format via `navigator.gpu.getPreferredCanvasFormat()`.

### 9.2 GPU Resources

* **Storage Texture** `outputTex: rgba8unorm` created with:

  * `usage = STORAGE_BINDING | TEXTURE_BINDING`
  * Size = canvas `W×H`
* **Uniforms**:

```wgsl
struct Uniforms {
  canvasW : u32; // width  in pixels
  canvasH : u32; // height in pixels
  tileSize: u32; // tile size in pixels
  tilesX  : u32; // tiles per row
  SS      : u32; // SSAA factor (1,2,4)
  _padA   : u32;
  _padB   : u32;
  _padC   : u32;
};
```

* **Shapes** (AoS):

```wgsl
struct Shape {
  vStart : u32;   // start index into 'vertices'
  vCount : u32;   // number of vertices
  rule   : u32;   // 0=evenodd, 1=nonzero
  _pad0  : u32;
  color  : vec4f; // premultiplied color in [0,1]
};
```

* **Bindings (group 0)**:

  * `@binding(0)`: `Uniforms` (UNIFORM)
  * `@binding(1)`: `shapes: array<Shape>` (STORAGE, read)
  * `@binding(2)`: `vertices: array<vec2f>` (STORAGE, read) — flattened vertex buffer
  * `@binding(3)`: `tileOffsets: array<u32>` (STORAGE, read)
  * `@binding(4)`: `tileCounts : array<u32>` (STORAGE, read)
  * `@binding(5)`: `tileShapeIx: array<u32>` (STORAGE, read)
  * `@binding(6)`: `outputTex: texture_storage_2d<rgba8unorm, write>`

**Memory layout notes**:

* `Uniforms` size = 8×4 = 32 bytes.
* `Shape` size = 4×4 (u32×4) + 16 (vec4f) = 32 bytes.
  Alignment satisfies WGSL rules (vec4 alignment is 16).

### 9.3 Compute Shader Logic (`workgroup_size(8,8,1)`)

For each **pixel** `(x,y)` (one invocation):

1. Compute tile index `t`:

   ```
   tX = x / tileSize
   tY = y / tileSize
   t  = tY * tilesX + tX
   start = tileOffsets[t]
   cnt   = tileCounts[t]
   ```
2. SSAA loop:

   ```
   accum = vec4(0)
   for sy in [0..SS-1]:
     for sx in [0..SS-1]:
       fx = x + (sx + 0.5)/SS
       fy = y + (sy + 0.5)/SS
       col = vec4(0) // premultiplied
       // Layer in global shape order for this tile:
       for k in [0..cnt-1]:
         s  = shapes[ tileShapeIx[start+k] ]
         inside = point_in_evenodd(...) or point_in_nonzero(...)
         if inside: col = over(s.color, col)
       accum += col
   avg = accum / (SS*SS)
   ```
3. Convert premultiplied → unpremultiplied for storage:

   ```
   A   = clamp(avg.a, 0, 1)
   rgb = (A > ε) ? clamp(avg.rgb / A, 0, 1) : 0
   textureStore(outputTex, (x,y), vec4(rgb, A))
   ```

**Fill tests**:

* **Even‑Odd** (half‑open rule):

```wgsl
inside = false
for i in 0..vCount-1, j = vCount-1:
  pi = vertices[vStart+i]
  pj = vertices[vStart+j]
  if ( (pi.y > y) != (pj.y > y) ):
     xin = (pj.x - pi.x)*(y - pi.y)/(pj.y - pi.y) + pi.x
     if (x < xin) inside = !inside
  j = i
```

* **Non‑Zero** (winding):

```wgsl
winding = 0
for i in 0..vCount-1, j = vCount-1:
  pi = vertices[vStart+i]
  pj = vertices[vStart+j]
  if (pi.y <= y && pj.y > y) { if (isLeft(pj,pi,(x,y)) > 0) winding++ }
  else if (pi.y > y && pj.y <= y) { if (isLeft(pj,pi,(x,y)) < 0) winding-- }
inside = (winding != 0)
```

* **Compositing** (premultiplied OVER):

  ```
  over(src,dst) = vec4( src.rgb + (1 - src.a)*dst.rgb,
                        src.a  + (1 - src.a)*dst.a )
  ```

### 9.4 Blit Pass

* Render pipeline draws a full‑screen **big triangle**.
* Fragment shader samples `outputTex` with nearest filtering and writes to the swapchain target.

---

## 10. CPU vs GPU Output Consistency

* **Layer order**: Both paths composite shapes in **global order** as collected into each tile’s list.
* **SSAA**: Both average **premultiplied** sub‑samples and only then convert to straight alpha.
* **Final storage**:

  * CPU: `ImageData` expects **straight alpha** → conversion done in `downsampleSSAA`.
  * GPU: storage texture also receives **straight alpha** values; the blit just copies them to the swapchain.
    The canvas is cleared opaque; blending with page background is not visible.

---

## 11. Animation & Timing

### 11.1 Timeline State

```ts
anim = {
  t: 0,               // seconds since play/reset
  dt: 0,              // seconds since last frame
  frame: 0,           // integer frame index
  t0: 0, last: 0,     // internal stamps
  lastFrameEnd: 0,    // wall-clock end of previous frame
  raf: 0,             // requestAnimationFrame id
  fpsEMA: 0           // smoothed FPS
}
```

### 11.2 Controls

* **Play/Pause** toggles RAF. On resume, `t0`/`last` are initialized from `performance.now()`.
* **Reset Time** sets `t=0,dt=0,frame=0` and re‑zeros metrics.
* **Render Gating**: `renderInFlight` prevents concurrent `draw()` calls (RAF + UI triggers).

### 11.3 FPS & Frame Time

* **Instant FPS** = `1000 / (ts - last)` where `ts` is RAF timestamp.
* **Smoothed FPS (EMA)**:

  ```
  fpsEMA = fpsEMA ? 0.9*fpsEMA + 0.1*inst : inst
  ```
* **Frame time (ms)** = `now - lastFrameEnd` measured **after** draw finishes (includes build, binning, and raster).

---

## 12. Error Handling & Fallbacks

* `opentype.js` load failure → stroke font fallback (`SIMPLE_FONT`), stroke‑based text rendering.
* WebGPU unavailable → renderer automatically falls back to CPU workers.
* User code exceptions → caught; an alert is shown; renderer continues with a minimal fallback shape set.

---

## 13. Numerical & Robustness Considerations

* **Flatten tolerances**: default `tol=0.35 px`, recursion capped at depth 10.
* **Half‑open scanline rule**: prevents double counting of vertices on scanlines (`(yi>y)!=(yj>y)`).
* **Degenerate edges**: zero‑length segments are skipped in stroke expansion; join caps guard against NaNs.
* **Miter**: intersection solved from offset lines; singular (parallel) cases → bevel fallback.
* **Tile inclusion**: bounding box discretization uses `floor(max/tileSize)`; shapes exactly on tile boundaries are considered in the “left/top” tile by construction.

---

## 14. Complexity & Performance Knobs

Let:

* `S` = number of shapes landing in a tile,
* `P` = pixels per tile (or per dispatch group),
* `K` = SSAA factor (`1`, `2`, or `4`), so samples per pixel = `K^2`.

**CPU**:

* Per tile: `O(S * P * (cost of fill rule))`. Even‑odd uses sort of intersections per scanline; average edges per scanline is typically small.
* Memory: supersampled float buffer of size `P * K^2 * 4 * 4 bytes`.
* Tunables: `tileSize`, `workers`, `SS`.

**GPU**:

* Per pixel: `O(S * K^2)` point‑in‑polygon tests.
* Tunables: `tileSize` (affects average `S` per tile), `SS`.

**Trade‑offs**:

* Larger `tileSize` → fewer tiles but more shapes per tile (`S↑`).
* More workers → higher CPU throughput until memory & main‑thread compositing becomes bottleneck.
* Higher `SS` → quadratic cost in samples.

---

## 15. Data Packing & Upload (GPU)

* **Vertices**: `Float32Array` of `(x,y)`, concatenated across shapes, `vStart` points into this array in units of **vertices**.
* **Shapes**: `Float32Array` as backing for both `Uint32Array` (first 16 bytes) and `Float32Array` (color vec4) via a shared buffer.
* **Uniforms / Storage** buffers are created **mappedAtCreation** and immediately **unmapped**; all arrays are uploaded per frame.

---

## 16. CPU Worker Algorithms (Detailed)

### 16.1 Even‑Odd Scanline Fill

For each scanline `gy`:

1. Build `xs[]`:

   * For each edge `(x0,y0)→(x1,y1)`, if `gy` crosses the edge under the half‑open test, compute:

     ```
     t  = (gy - y0) / (y1 - y0)
     x  = x0 + t * (x1 - x0)
     xs.push(x)
     ```
2. Sort `xs` ascending.
3. Paint spans for pairs `(xs[2k], xs[2k+1])`.

### 16.2 Non‑Zero Scanline Fill

For each scanline `gy`:

1. Build events:

   ```
   if up-crossing:   push { x, w:+1 }
   if down-crossing: push { x, w:-1 }
   ```
2. Sort events by `x`.
3. Sweep:

   ```
   W=0; spanStart = null
   for each group of equal x:
     W += sum(w)
     if prevW==0 && W!=0: spanStart = x
     if prevW!=0 && W==0: fill [spanStart, x)
   ```

### 16.3 Premultiplied Compositing

For each pixel (or sub‑pixel) sample:

```
dst.rgb = src.rgb + (1 - src.a)*dst.rgb
dst.a   = src.a   + (1 - src.a)*dst.a
```

### 16.4 SSAA Downsample

For base pixel `(x,y)`:

```
(r,g,b,a) = average over SS×SS samples (premultiplied)
if a > ε: (r,g,b) = (r,g,b)/a
pack to Uint8ClampedArray
```

---

## 17. WGSL Shaders (Interface Contract)

### 17.1 Compute Shader I/O

* **Inputs** (read):

  * `Uniforms`
  * `shapes[]`
  * `vertices[]`
  * `tileOffsets[]`
  * `tileCounts[]`
  * `tileShapeIx[]`
* **Outputs** (write):

  * `outputTex` (RGBA8 unorm; **straight alpha** value convention).

### 17.2 Workgroup & Dispatch

* `@workgroup_size(8,8,1)`
  Dispatch grid:

  ```
  dispatchX = ceil(W / 8)
  dispatchY = ceil(H / 8)
  ```
* Each **invocation** maps to exactly one pixel.

### 17.3 Numerical Rules

* **Half‑open edge test** for even‑odd matches CPU.
* **Winding** uses signed isLeft test (2D cross product).
* **Color** in shapes is **premultiplied** before upload to GPU buffers.

---

## 18. UI / Controls / Defaults

* **Renderer**: `auto|webgpu|cpu`, default `auto` (uses WebGPU if available).
* **Canvas Size**: presets (800×600, 1024×768, 1280×720, 1920×1080).
* **Tile Size**: `[16..128]`, default `64`.
* **Workers**: `[1..hardwareConcurrency]`, default `4`.
* **Stroke Width**: `[1..40]`, default `10`.
* **Join**: `round|bevel|miter`, default `round`.
* **Cap**: `round|butt|square`, default `round`.
* **Miter Limit**: `[1..10]`, step `0.25`, default `4`.
* **AA (SSAA)**: `1×|2×|4×`, default `2×`.
* **Rule Toggle**: cycles `evenodd ↔ nonzero`.
* **Animation**: Play/Pause, Reset Time, Load Animation Sample (keeps the original playground as default).

---

## 19. Safety, Reentrancy, and Resource Lifetime

* **renderInFlight**: guards against overlapping `draw()` calls (RAF + UI events).
* **Workers**: persistent pool; size adjusted on demand. (Note: Blob URL for worker script is created once per session.)
* **WebGPU**:

  * Pipelines created once; reused every frame.
  * Output storage texture is re‑created on canvas size changes.
  * `device.queue.onSubmittedWorkDone()` awaits completion for accurate timing.
* **Fonts**: cached in `globalThis.__plex` across frames.

---

## 20. Known Limitations / Notes

* **No per‑tile geometric clipping**: Full polygons are evaluated per tile; correct but could be optimized.
* **Exact closure check** relies on `closePath()`; hand‑built polylines must close explicitly to be filled.
* **Color space**: Operates in linearized float math; the storage/buffer is unorm; the page background is opaque, so premul/straight final blending with the page is not visually relevant.
* **Precision**: Float32 raster; numeric tolerances (e.g., `ε≈1e‑6`) are used to avoid division by zero and equality edge cases.

---

## 21. Extensibility Hooks (non‑functional spec)

* **Tile‑local clipping** to reduce point‑in‑polygon work for large shapes.
* **Per‑shape Z‑order** independent of emission order.
* **Coverage AA** (analytical) as an alternative to SSAA for speed.
* **Path caching** across frames for stable animation geometry where only transforms change.
* **MSAA/Resolve** on GPU surfaces when supported and appropriate.

---

## Appendix A — Data Structures Summary

```txt
// JS (main)
Array<Shape> shapes
  Shape.verts: Float32Array (x0,y0,x1,y1,...)
  Shape.color: Uint8ClampedArray [r,g,b,a]
  Shape.rule : 'evenodd'|'nonzero'

Tiling:
  counts[tileCount]    : Uint32Array
  offsets[tileCount]   : Uint32Array // exclusive scan of counts
  tileShapeIndex[sum]  : Uint32Array // flattened indices per tile
```

```wgsl
// GPU (group(0))
binding(0)  Uniforms                    // u32[8]
binding(1)  array<Shape>                // 32 bytes per shape
binding(2)  array<vec2f> vertices       // flattened vertices
binding(3)  array<u32> tileOffsets
binding(4)  array<u32> tileCounts
binding(5)  array<u32> tileShapeIx
binding(6)  texture_storage_2d<rgba8unorm, write> outputTex
```

---

## Appendix B — Key Formulae

* **Normal** of vector `(dx,dy)`:

  ```
  n = (-dy, dx) / sqrt(dx^2 + dy^2)   // unit-length
  ```
* **Line intersection** (for miter):

  ```
  p0 + t * v0  intersects  p1 + u * v1
  t = cross(p1-p0, v1) / cross(v0, v1)
  ```
* **isLeft test** (signed area):

  ```
  isLeft(a,b,p) = (b.x - a.x)*(p.y - a.y) - (b.y - a.y)*(p.x - a.x)
  ```

---

## Appendix C — Timeline Pseudocode

```txt
function loop(ts):
  dt_ms   = ts - anim.last
  anim.dt = max(0, dt_ms)/1000
  anim.t  = (ts - anim.t0)/1000
  anim.frame++

  instFPS     = (dt_ms > 0) ? 1000 / dt_ms : 0
  fpsEMA      = fpsEMA ? 0.9*fpsEMA + 0.1*instFPS : instFPS

  await draw() // gated by renderInFlight

  frameDur    = now() - anim.lastFrameEnd
  anim.last   = ts
  anim.lastFrameEnd = now()

  update UI(FPS=fpsEMA, frameTime=frameDur)
  if playing: requestAnimationFrame(loop)
```

---

**End of specification.**
