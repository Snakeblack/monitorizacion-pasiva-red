package monitoring;

import java.nio.charset.StandardCharsets;
import java.util.Base64;
import java.util.LinkedHashMap;
import java.util.Map;
import org.apache.kafka.connect.data.Schema;
import org.apache.kafka.connect.errors.DataException;
import org.apache.kafka.connect.header.ConnectHeaders;
import org.apache.kafka.connect.sink.SinkRecord;

/** Dependency-free checks for the pure sink guard; exits non-zero on the first failed expectation. */
public final class SessionContractTest {
    private static final String SEARCH_ID = "0b1c2d3e-4f50-4a61-8b72-93a4b5c6d7e8";
    private static int checks;

    public static void main(String[] args) {
        String longSite = "s".repeat(128), longSensor = "n".repeat(128), longEvent = "e".repeat(128);

        SinkRecord upsert = apply(record(key("site", "sensor", "event"), upsert("site", "sensor", "event", SEARCH_ID), 1L));
        expect("upsert is re-keyed to the compact search identity", SEARCH_ID.equals(upsert.key()));
        expect("search identity is 36 bytes", SEARCH_ID.getBytes(StandardCharsets.UTF_8).length == 36);
        expect("upsert value is delivered unchanged", "event".equals(((Map<?, ?>) upsert.value()).get("eventId")));
        expect("revision header survives the key rewrite", Long.valueOf(1L).equals(upsert.headers().lastWithName("revision").value()));
        expect("topic and offset survive", "monitoring.sessions.v2".equals(upsert.topic()) && upsert.kafkaOffset() == 7L);

        String fullKey = key(longSite, longSensor, longEvent);
        expect("maximum identifiers exceed the Elasticsearch _id limit", fullKey.getBytes(StandardCharsets.UTF_8).length == 515);
        SinkRecord maximum = apply(record(fullKey, upsert(longSite, longSensor, longEvent, SEARCH_ID), 1L));
        expect("a 515-byte identity is indexed under the 36-byte key", SEARCH_ID.equals(maximum.key()));
        expect("the full document key stays in the document", fullKey.equals(((Map<?, ?>) maximum.value()).get("documentKey")));

        Map<String, Object> delete = identity("site", "sensor", "event", SEARCH_ID);
        delete.put("operation", "delete");
        expect("delete barrier is re-keyed too", SEARCH_ID.equals(apply(record(key("site", "sensor", "event"), delete, 1L)).key()));

        Map<String, Object> legacy = upsert("site", "sensor", "event", SEARCH_ID);
        legacy.put("schemaVersion", 1);
        legacy.remove("searchDocumentId");
        rejects("legacy schema 1 is isolated", "contract-version", record(key("site", "sensor", "event"), legacy, 1L));

        Map<String, Object> missing = upsert("site", "sensor", "event", SEARCH_ID);
        missing.remove("searchDocumentId");
        rejects("missing search identity", "contract-fields", record(key("site", "sensor", "event"), missing, 1L));

        for (String bad : new String[] { "", "not-a-uuid", SEARCH_ID.toUpperCase(), SEARCH_ID + "0", SEARCH_ID.replace("-", ""), "{" + SEARCH_ID + "}" })
            rejects("malformed search identity '" + bad + "'", "contract-search-id",
                record(key("site", "sensor", "event"), upsert("site", "sensor", "event", bad), 1L));
        Map<String, Object> numeric = upsert("site", "sensor", "event", SEARCH_ID);
        numeric.put("searchDocumentId", 7);
        rejects("non-text search identity", "contract-search-id", record(key("site", "sensor", "event"), numeric, 1L));

        rejects("record key must be the full document key", "contract-identity",
            record(SEARCH_ID, upsert("site", "sensor", "event", SEARCH_ID), 1L));
        Map<String, Object> forged = upsert("site", "sensor", "event", SEARCH_ID);
        forged.put("documentKey", key("site", "sensor", "other"));
        rejects("document key must match the identity fields", "contract-identity", record(key("site", "sensor", "event"), forged, 1L));
        rejects("revision header must match", "contract-revision-header", record(key("site", "sensor", "event"), upsert("site", "sensor", "event", SEARCH_ID), 2L));

        System.out.println(checks + " checks passed");
    }

    private static SinkRecord apply(SinkRecord record) {
        try (SessionContract<SinkRecord> contract = new SessionContract<>()) {
            return contract.apply(record);
        }
    }

    private static void rejects(String name, String code, SinkRecord record) {
        try {
            apply(record);
        } catch (DataException exception) {
            expect(name + " (" + code + ")", code.equals(exception.getMessage()));
            return;
        }
        throw new AssertionError("Expected rejection: " + name);
    }

    private static void expect(String name, boolean condition) {
        if (!condition) throw new AssertionError("Failed: " + name);
        checks++;
    }

    private static SinkRecord record(String key, Map<String, Object> value, long header) {
        ConnectHeaders headers = new ConnectHeaders();
        headers.addLong("revision", header);
        return new SinkRecord("monitoring.sessions.v2", 0, Schema.OPTIONAL_STRING_SCHEMA, key, null, value, 7L, 1L,
            org.apache.kafka.common.record.TimestampType.CREATE_TIME, headers);
    }

    private static Map<String, Object> identity(String site, String sensor, String event, String searchId) {
        Map<String, Object> value = new LinkedHashMap<>();
        value.put("schemaVersion", 2);
        value.put("operation", "upsert");
        value.put("documentKey", key(site, sensor, event));
        value.put("searchDocumentId", searchId);
        value.put("revision", 1L);
        value.put("siteId", site);
        value.put("sensorId", sensor);
        value.put("eventId", event);
        return value;
    }

    private static Map<String, Object> upsert(String site, String sensor, String event, String searchId) {
        Map<String, Object> value = identity(site, sensor, event, searchId);
        value.put("startedAt", "2026-10-05T10:00:00.100Z");
        value.put("endedAt", "2026-10-05T10:00:01.000Z");
        value.put("sourceIp", "2001:db8::1");
        value.put("destinationIp", "192.0.2.2");
        value.put("sourcePort", 1234);
        value.put("destinationPort", 443);
        value.put("protocol", "TCP");
        value.put("vlanId", null);
        value.put("provenance", "synthetic");
        value.put("inferred", null);
        value.put("partial", null);
        value.put("closeReason", null);
        value.put("packetCount", null);
        value.put("byteCount", null);
        value.put("acceptedAt", "2026-10-05T10:00:02.000Z");
        return value;
    }

    private static String key(String site, String sensor, String event) {
        Base64.Encoder encoder = Base64.getUrlEncoder().withoutPadding();
        return encoder.encodeToString(site.getBytes(StandardCharsets.UTF_8)) + "." + encoder.encodeToString(sensor.getBytes(StandardCharsets.UTF_8))
            + "." + encoder.encodeToString(event.getBytes(StandardCharsets.UTF_8));
    }
}
