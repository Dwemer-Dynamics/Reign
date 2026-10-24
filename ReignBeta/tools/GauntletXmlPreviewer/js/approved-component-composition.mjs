import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
export function composeApprovedComponents({spec, stateId, manifest, workspaceRoot, decodePng, paletteTokens}) {
  if (spec.schema !== 'reign-approved-component-composition-v1' || spec.canvas?.width !== 1672 || spec.canvas?.height !== 941)
    throw new Error('Unsupported approved component composition canvas or schema.');
  const state = spec.states?.[stateId];
  if (!state || !Array.isArray(state.layers) || !state.layers.length || !Array.isArray(state.exclusions))
    throw new Error('The exact state requires declared component layers and exclusions: ' + stateId);
  const width = spec.canvas.width, height = spec.canvas.height;
  const data = new Uint8Array(width * height * 4);
  const components = [];
  for (const layer of state.layers) {
    if (![layer.x,layer.y,layer.width,layer.height].every(Number.isInteger) || layer.x < 0 || layer.y < 0
        || layer.width < 1 || layer.height < 1 || layer.x + layer.width > width || layer.y + layer.height > height)
      throw new Error('A declared component escapes its fixed canvas.');
    let source;
    if (layer.colorToken) {
      const color = paletteTokens?.[layer.colorToken];
      if (!/^#[0-9a-f]{8}$/i.test(color || '')) throw new Error('An approved palette token is required.');
      source = {width:layer.width,height:layer.height,data:new Uint8Array(layer.width*layer.height*4)};
      const rgba=[1,3,5,7].map(offset=>parseInt(color.slice(offset,offset+2),16));
      for(let pixel=0;pixel<source.data.length;pixel+=4) source.data.set(rgba,pixel);
    } else {
      if (!String(layer.assetId).startsWith('reign_government_v3_')) throw new Error('A registered Government component is required.');
      const asset = manifest.generatedRuntimeAssets?.find(entry=>entry.id===layer.assetId);
      if (!asset || !asset.sha256 || !asset.compositionContract?.includes('government-sessions-v3/composition-contract.json'))
        throw new Error('Component lacks its approved source registration: ' + layer.assetId);
      const file = path.resolve(workspaceRoot,asset.path);
      const relative = path.relative(workspaceRoot,file);
      if (relative.startsWith('..') || path.isAbsolute(relative)) throw new Error('Approved component escapes the workspace.');
      const contractFile=path.resolve(workspaceRoot,asset.compositionContract);
      const contractRelative=path.relative(workspaceRoot,contractFile);
      if(contractRelative.startsWith('..') || path.isAbsolute(contractRelative)) throw new Error('Approved source contract escapes the workspace.');
      const contract=JSON.parse(fs.readFileSync(contractFile,'utf8'));
      const original=contract.assets?.find(entry=>entry.id===asset.id);
      if(contract.schema!=='reign-government-sessions-composition-v1' || !original || original.sha256!==asset.sha256
        || original.width!==layer.width || original.height!==layer.height)
        throw new Error('Component differs from its approved source composition: '+asset.id);
      const bytes=fs.readFileSync(file);
      if(hash(bytes)!==asset.sha256) throw new Error('Approved component hash mismatch: ' + layer.assetId);
      source=decodePng(bytes);
      if(source.width!==layer.width || source.height!==layer.height)
        throw new Error('Approved components retain their exact intrinsic dimensions: ' + layer.assetId);
      components.push({id:layer.assetId,sha256:asset.sha256,x:layer.x,y:layer.y,width:layer.width,height:layer.height});
    }
    for(let y=0;y<source.height;y++) for(let x=0;x<source.width;x++) {
      const from=(y*source.width+x)*4, to=((layer.y+y)*width+layer.x+x)*4;
      const alpha=source.data[from+3]/255, prior=data[to+3]/255, combined=alpha+prior*(1-alpha);
      if(combined===0) continue;
      for(let channel=0;channel<3;channel++) data[to+channel]=Math.round((source.data[from+channel]*alpha+data[to+channel]*prior*(1-alpha))/combined);
      data[to+3]=Math.round(combined*255);
    }
  }
  for(const exclusion of state.exclusions) {
    if(!['dynamic-text','portrait-or-scene','unprovided-state'].includes(exclusion.category) || exclusion.shape!=='rectangle'
        || ![exclusion.x,exclusion.y,exclusion.width,exclusion.height].every(Number.isFinite)
        || exclusion.width<=0 || exclusion.height<=0 || exclusion.x<0 || exclusion.y<0
        || exclusion.x+exclusion.width>width || exclusion.y+exclusion.height>height)
      throw new Error('Only bounded declared text, graphics and state exclusions are supported.');
    if(components.some(component=>component.width<width && exclusion.x<=component.x && exclusion.y<=component.y
      && exclusion.x+exclusion.width>=component.x+component.width && exclusion.y+exclusion.height>=component.y+component.height))
      throw new Error('A whole owning component cannot be masked.');
  }
  return {image:{width,height,data},exclusions:state.exclusions,evidence:{schema:spec.schema,stateId,
    authority:'Unchanged approved component pixels in an explicitly declared state layout; no replacement artwork or tolerance changes.',
    components,compositionSha256:hash(Buffer.from(JSON.stringify(spec)))}};
}
