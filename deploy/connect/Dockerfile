ARG KAFKA_IMAGE
FROM ${KAFKA_IMAGE}
USER root
COPY vendor/debezium-connector-postgres-3.6.3.Final-plugin.tar.gz /tmp/debezium.tar.gz
COPY vendor/confluentinc-kafka-connect-elasticsearch-16.0.0.zip /tmp/elasticsearch.zip
RUN echo "68dfbd3aa0e22cbc164017311449f69dfe6b96b70e72c72c7a3a24ef5c7810e0  /tmp/debezium.tar.gz" | sha256sum -c - \
 && echo "3e658470966e1b419c349850a9db9da124fd8764e4d93710bb49b202ab51ba1e  /tmp/elasticsearch.zip" | sha256sum -c - \
 && mkdir -p /opt/monitoring/plugins/debezium /opt/monitoring/plugins/elasticsearch \
 && tar -xzf /tmp/debezium.tar.gz --strip-components=1 -C /opt/monitoring/plugins/debezium \
 && unzip -q /tmp/elasticsearch.zip -d /opt/monitoring/plugins/elasticsearch \
 && rm /tmp/debezium.tar.gz /tmp/elasticsearch.zip
COPY vendor/jmx_prometheus_javaagent-1.0.1.jar /opt/monitoring/jmx_prometheus_javaagent.jar
RUN echo "7d61f737fd661610ccc14aea79764faa1ea94a340cbc8f0029b3d2edea3d80c1  /opt/monitoring/jmx_prometheus_javaagent.jar" | sha256sum -c -
COPY jmx-connect.yaml /opt/monitoring/
COPY connect.properties health.sh entrypoint.sh /opt/monitoring/
COPY transforms/build/session-contract.jar /opt/monitoring/plugins/elasticsearch/confluentinc-kafka-connect-elasticsearch-16.0.0/lib/
USER appuser
ENTRYPOINT ["bash", "/opt/monitoring/entrypoint.sh"]
