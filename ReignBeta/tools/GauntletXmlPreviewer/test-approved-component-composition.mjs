import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import crypto from 'node:crypto';
import {composeApprovedComponents} from './js/approved-component-composition.mjs';

const scratch=fs.mkdtempSync(path.join(os.tmpdir(),'reign-approved-components-'));
try {
  const bytes=Buffer.from('owned approved sprite fixture');
  fs.writeFileSync(path.join(scratch,'sprite.png'),bytes);
  const asset={id:'reign_government_v3_fixture',path:'sprite.png',sha256:crypto.createHash('sha256').update(bytes).digest('hex'),compositionContract:'government-sessions-v3/composition-contract.json'};
  const manifest={generatedRuntimeAssets:[asset]};
  fs.mkdirSync(path.join(scratch,'government-sessions-v3'));
  const contractPath=path.join(scratch,asset.compositionContract);
  fs.writeFileSync(contractPath,JSON.stringify({schema:'reign-government-sessions-composition-v1',assets:[{...asset,width:2,height:2}]}));
  const spec={schema:'reign-approved-component-composition-v1',canvas:{width:1672,height:941},states:{state:{
    layers:[{colorToken:'canvasBlack',x:0,y:0,width:1672,height:941},{assetId:asset.id,x:5,y:7,width:2,height:2}],exclusions:[]}}};
  const options={spec,stateId:'state',manifest,workspaceRoot:scratch,paletteTokens:{canvasBlack:'#030303FF'},
    decodePng:()=>({width:2,height:2,data:Uint8Array.from([80,60,40,255,80,60,40,255,80,60,40,255,80,60,40,255])})};
  const before=JSON.stringify(spec), composed=composeApprovedComponents(options);
  assert.deepEqual([...composed.image.data.slice((7*1672+5)*4,(7*1672+5)*4+4)],[80,60,40,255]);
  assert.deepEqual([...composed.image.data.slice(0,4)],[3,3,3,255]);
  assert.equal(JSON.stringify(spec),before,'Auditing never rewrites the declared layout.');
  assert.equal(composed.evidence.components[0].sha256,asset.sha256);
  const changed=edit=>{const copy=structuredClone(spec);edit(copy.states.state);return {...options,spec:copy};};
  assert.throws(()=>composeApprovedComponents({...options,stateId:'another'}),/exact state/);
  assert.throws(()=>composeApprovedComponents(changed(s=>s.layers[1].x=1672)),/escapes/);
  assert.throws(()=>composeApprovedComponents(changed(s=>s.layers[1].width=3)),/approved source composition/);
  assert.throws(()=>composeApprovedComponents(changed(s=>s.layers[1].assetId='unregistered')),/registered Government/);
  assert.throws(()=>composeApprovedComponents({...options,manifest:{generatedRuntimeAssets:[]}}),/source registration/);
  assert.throws(()=>composeApprovedComponents({...options,manifest:{generatedRuntimeAssets:[{...asset,sha256:'0'.repeat(64)}]}}),/approved source composition/);
  fs.writeFileSync(path.join(scratch,'sprite.png'),'changed unapproved pixels');
  assert.throws(()=>composeApprovedComponents(options),/hash mismatch/);
  fs.writeFileSync(path.join(scratch,'sprite.png'),bytes);
  assert.throws(()=>composeApprovedComponents({...options,manifest:{generatedRuntimeAssets:[{...asset,path:'../sprite.png'}]}}),/escapes the workspace/);
  assert.throws(()=>composeApprovedComponents(changed(s=>s.layers[0].colorToken='invented')),/palette token/);
  assert.throws(()=>composeApprovedComponents(changed(s=>s.exclusions.push({id:'hide-card',category:'dynamic-text',shape:'rectangle',x:5,y:7,width:2,height:2}))),/whole owning component/);
  assert.throws(()=>composeApprovedComponents(changed(s=>s.exclusions.push({id:'outside',category:'dynamic-text',shape:'rectangle',x:1672,y:0,width:2,height:2}))),/bounded declared/);
  console.log('Approved component composition: PASS (pixels, palette, immutable layout, identity, bounds, dimensions, hashes and exposed owning frames).');
} finally {
  const resolved=fs.realpathSync(scratch), parent=fs.realpathSync(os.tmpdir());
  if(!resolved.startsWith(parent+path.sep) || !path.basename(resolved).startsWith('reign-approved-components-')) throw new Error('Unsafe test cleanup path.');
  fs.rmSync(resolved,{recursive:true,force:true});
}
