import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { http, until } from './compose.mjs';

export async function configurePipeline() {
  const mapping=JSON.parse(readFileSync('deploy/elasticsearch/sessions-v1.json','utf8'));
  let response=http('elasticsearch:9200','/sessions-v1-000001');
  if(response.status===404) {
    response=http('elasticsearch:9200','/sessions-v1-000001','PUT',mapping);
    if(response.status!==200) throw new Error(`Index creation failed: ${JSON.stringify(response)}`);
  } else if(response.status!==200) throw new Error('Search dependency is unavailable.');
  response=http('elasticsearch:9200','/_aliases','POST',{actions:[{add:{index:'sessions-v1-000001',alias:'sessions-read'}}]});
  if(response.status!==200) throw new Error('Read alias installation failed.');
  for(const name of ['source','sink']) {
    const config=JSON.parse(readFileSync(`deploy/connect/${name}.json`,'utf8'));
    response=http('connect:8083',`/connectors/monitoring-${name}/config`,'PUT',config);
    if(response.status!==200&&response.status!==201) throw new Error(`Connector ${name} rejected: ${JSON.stringify(response)}`);
    await until(()=>http('connect:8083',`/connectors/monitoring-${name}/status`), x=>x.status===200&&x.body.connector.state==='RUNNING'&&x.body.tasks.length===1&&x.body.tasks.every(t=>t.state==='RUNNING'));
  }
}
if(import.meta.url===pathToFileURL(process.argv[1]).href) await configurePipeline();
