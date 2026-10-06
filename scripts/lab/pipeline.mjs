import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { http, until } from './compose.mjs';

const index = 'sessions-v2-000001';
const family = 'sessions-v2-';
const alias = 'sessions-read';

export async function configurePipeline() {
  const mapping=JSON.parse(readFileSync('deploy/elasticsearch/sessions-v2.json','utf8'));
  let response=http('elasticsearch:9200',`/${index}`);
  if(response.status===404) {
    response=http('elasticsearch:9200',`/${index}`,'PUT',mapping);
    if(response.status!==200) throw new Error(`Index creation failed: ${JSON.stringify(response)}`);
  } else if(response.status!==200) throw new Error('Search dependency is unavailable.');
  // Legacy generations (sessions-v1-*, keyed by the over-long document key) leave the alias; a rebuilt v2 generation is kept.
  response=http('elasticsearch:9200',`/_alias/${alias}`);
  if(response.status!==200&&response.status!==404) throw new Error('Read alias inspection failed.');
  const attached=response.status===200?Object.keys(response.body):[];
  const actions=attached.filter(name=>!name.startsWith(family)).map(name=>({remove:{index:name,alias}}));
  if(!attached.some(name=>name.startsWith(family))) actions.push({add:{index,alias}});
  if(actions.length>0) {
    response=http('elasticsearch:9200','/_aliases','POST',{actions});
    if(response.status!==200) throw new Error('Read alias installation failed.');
  }
  for(const name of ['source','sink']) {
    const config=JSON.parse(readFileSync(`deploy/connect/${name}.json`,'utf8'));
    response=http('connect:8083',`/connectors/monitoring-${name}/config`,'PUT',config);
    if(response.status!==200&&response.status!==201) throw new Error(`Connector ${name} rejected: ${JSON.stringify(response)}`);
    await until(()=>http('connect:8083',`/connectors/monitoring-${name}/status`), x=>x.status===200&&x.body.connector.state==='RUNNING'&&x.body.tasks.length===1&&x.body.tasks.every(t=>t.state==='RUNNING'));
  }
}
if(import.meta.url===pathToFileURL(process.argv[1]).href) await configurePipeline();
