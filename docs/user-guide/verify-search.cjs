// Functional search acceptance: real content + player-worded queries.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {rank,normalize}=require('./guide.js');
const data=JSON.parse(fs.readFileSync(path.join(__dirname,'search-data.json'),'utf8'));
const cases=[
 ['wife','marriage-and-npc-matchmaking'],
 ['how can I have a baby','having-a-baby-the-important-change'],
 ["why can't I have kids",'having-a-baby-the-important-change'],
 ['ButterLib','what-you-need-before-playing'],
 ['RAM','ram-plan-for-the-game-and-the-server-together'],
 ['cloud image provider','what-you-need-before-playing'],
 ['become senator','joining-government-without-a-crown'],
 ["NPC won't leave",'inviting-a-noble-to-travel-with-you'],
 ['troops expensive','troops-cost-more-than-in-native-bannerlord'],
 ['Whoremonger','visit-the-madam'],
 ['letters','correspondence-sending-and-receiving-letters'],
 ['Ctrl+M','the-controls-worth-knowing-first'],
 ['Alt+A','asking-for-a-real-action'],
 ['homes','visit-the-homes'],
 ['garrison wages','garrisons-patrols-and-demobilization'],
 ['Pestilence','kingdom-catastrophes-and-boons'],
 ['mutual watch','clan-accords'],
 ['leave spouse by letter','marriage-and-npc-matchmaking'],
 ['portrait refresh','portraits-and-look-at-them'],
 ['private practice duel','choosing-a-duel'],
 ['save warning','a-bannerlord-save-and-reign-memory-belong-together'],
 ['peace standing','peace-standing-war-standing-and-garrisons'],
 ['War Sails','ships-ports-and-military-purchases'],
 ['rumours','gossip-is-not-proof'],
 ['Story Location','approach-invitations-and-personal-stories'],
 ['release rescind','arrests-accusation-confirmation-and-custody'],
 ['foreign governor','small-interface-and-native-behavior-changes'],
 ['Training Yard','training-yard'],
 ['instalments','terms-conditions-and-ongoing-agreements'],
 ['Personal leave','personal-leave-and-changing-plans'],
 ['party announcement','party-announcements'],
];
for(const [q,id] of cases){const results=rank(data,q);assert(results.slice(0,3).some(t=>t.id===id),`Query ${JSON.stringify(q)}: wanted ${id}, got ${results.slice(0,3).map(t=>t.id)}`);}
assert.deepEqual(rank(data,''),[]);
assert.deepEqual(rank(data,'the and of'),[]);
assert.deepEqual(rank(data,'zxqvnonexistent'),[]);
assert.deepEqual(rank(data,'<img src=x onerror=alert(1)>'),[]);
assert.equal(normalize('NPC won’t leave'),'npc wont leave');
assert(rank(data,'CLAN ACCORDS')[0].id==='clan-accords');
const preview=rank(data,'become senator').find(t=>t.id==='joining-government-without-a-crown');assert(preview.preview);
console.log(JSON.stringify({ok:true,playerQueries:cases.length,edgeCases:7,topics:data.length}));
