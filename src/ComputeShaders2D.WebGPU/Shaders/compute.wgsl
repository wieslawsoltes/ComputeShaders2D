struct Uniforms {
  canvasW : u32,
  canvasH : u32,
  tileSize: u32,
  tilesX  : u32,
  SS      : u32,
  _padA   : u32,
  _padB   : u32,
  _padC   : u32,
};

struct Shape {
  vStart : u32,
  vCount : u32,
  rule   : u32,
  _pad0  : u32,
  color  : vec4<f32>,
  clipStart : u32,
  clipCount : u32,
  maskStart : u32,
  maskCount : u32,
  opacity   : f32,
  _pad1     : f32,
  _pad2     : f32,
  _pad3     : f32,
};

struct ClipPoly {
  vStart : u32,
  vCount : u32,
  rule   : u32,
  _pad0  : u32,
};

struct MaskPoly {
  vStart : u32,
  vCount : u32,
  rule   : u32,
  _pad0  : u32,
  alpha  : f32,
  _pad1  : f32,
  _pad2  : f32,
  _pad3  : f32,
};

@group(0) @binding(0) var<uniform> uni : Uniforms;
@group(0) @binding(1) var<storage, read> shapes : array<Shape>;
@group(0) @binding(2) var<storage, read> vertices : array<vec2<f32>>;
@group(0) @binding(3) var<storage, read> tileOC : array<u32>;
@group(0) @binding(4) var<storage, read> tileShapeIx : array<u32>;
@group(0) @binding(5) var<storage, read> clips : array<ClipPoly>;
@group(0) @binding(6) var<storage, read> masks : array<MaskPoly>;
@group(0) @binding(7) var<storage, read> refs : array<u32>;
@group(0) @binding(8) var outputTex : texture_storage_2d<rgba8unorm, write>;

fn over(src: vec4<f32>, dst: vec4<f32>) -> vec4<f32> {
  let ida = 1.0 - src.a;
  return vec4<f32>(src.rgb + ida * dst.rgb, src.a + ida * dst.a);
}

fn is_left(a: vec2<f32>, b: vec2<f32>, p: vec2<f32>) -> f32 {
  return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
}

fn inside_evenodd(start:u32, count:u32, point:vec2<f32>) -> bool {
  var inside = false;
  var i = 0u;
  var j = count - 1u;
  loop {
    if (i >= count) { break; }
    let pi = vertices[start + i];
    let pj = vertices[start + j];
    let cond = ( (pi.y > point.y) != (pj.y > point.y) );
    if (cond) {
      let xin = (pj.x - pi.x) * (point.y - pi.y) / (pj.y - pi.y) + pi.x;
      if (point.x < xin) { inside = !inside; }
    }
    j = i;
    i = i + 1u;
  }
  return inside;
}

fn inside_nonzero(start:u32, count:u32, point:vec2<f32>) -> bool {
  var winding = 0;
  var i = 0u;
  var j = count - 1u;
  loop {
    if (i >= count) { break; }
    let pi = vertices[start + i];
    let pj = vertices[start + j];
    if (pi.y <= point.y) {
      if (pj.y > point.y && is_left(pi, pj, point) > 0.0) { winding = winding + 1; }
    } else {
      if (pj.y <= point.y && is_left(pi, pj, point) < 0.0) { winding = winding - 1; }
    }
    j = i;
    i = i + 1u;
  }
  return winding != 0;
}

fn inside_path(start:u32, count:u32, rule:u32, point:vec2<f32>) -> bool {
  return select(inside_nonzero(start, count, point), inside_evenodd(start, count, point), rule == 0u);
}

@compute @workgroup_size(8, 8, 1)
fn main(@builtin(global_invocation_id) gid: vec3<u32>) {
  if (gid.x >= uni.canvasW || gid.y >= uni.canvasH) { return; }
  let tileSizeF = f32(uni.tileSize);
  let tilesX = max(1u, uni.tilesX);
  let tX = min(gid.x / uni.tileSize, tilesX - 1u);
  let tY = gid.y / uni.tileSize;
  let tileIndex = tY * tilesX + tX;
  let meta = tileIndex * 2u;
  let start = tileOC[meta + 0u];
  let count = tileOC[meta + 1u];

  var accum = vec4<f32>(0.0);
  let SS = max(1u, uni.SS);
  let samples = f32(SS * SS);

  var sy = 0u;
  loop {
    if (sy >= SS) { break; }
    var sx = 0u;
    loop {
      if (sx >= SS) { break; }
      let fx = f32(gid.x) + (f32(sx) + 0.5) / f32(SS);
      let fy = f32(gid.y) + (f32(sy) + 0.5) / f32(SS);
      var color = vec4<f32>(0.0);
      var k = 0u;
      loop {
        if (k >= count) { break; }
        let shapeIndex = tileShapeIx[start + k];
        let sh = shapes[shapeIndex];
        if (inside_path(sh.vStart, sh.vCount, sh.rule, vec2<f32>(fx, fy))) {
          var ok = true;
          var c = 0u;
          loop {
            if (c >= sh.clipCount) { break; }
            let cid = refs[sh.clipStart + c];
            let cp = clips[cid];
            if (!inside_path(cp.vStart, cp.vCount, cp.rule, vec2<f32>(fx, fy))) {
              ok = false;
              break;
            }
            c = c + 1u;
          }

          if (ok) {
            var maskValue = 1.0;
            if (sh.maskCount > 0u) {
              maskValue = 0.0;
              var mi = 0u;
              loop {
                if (mi >= sh.maskCount) { break; }
                let mid = refs[sh.maskStart + mi];
                let mp = masks[mid];
                if (inside_path(mp.vStart, mp.vCount, mp.rule, vec2<f32>(fx, fy))) {
                  maskValue = maskValue + (1.0 - maskValue) * clamp(mp.alpha, 0.0, 1.0);
                }
                mi = mi + 1u;
              }
            }
            let factor = sh.opacity * maskValue;
            if (factor > 0.00001) {
              color = over(sh.color * factor, color);
            }
          }
        }
        k = k + 1u;
      }
      accum = accum + color;
      sx = sx + 1u;
    }
    sy = sy + 1u;
  }

  let avg = accum / samples;
  let A = clamp(avg.a, 0.0, 1.0);
  var rgb = vec3<f32>(0.0);
  if (A > 0.00001) {
    rgb = clamp(avg.rgb / A, vec3<f32>(0.0), vec3<f32>(1.0));
  }
  textureStore(outputTex, vec2<i32>(i32(gid.x), i32(gid.y)), vec4<f32>(rgb, A));
}
