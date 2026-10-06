package monitoring;

import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.Base64;
import java.util.Map;
import java.util.Set;
import org.apache.kafka.common.config.ConfigDef;
import org.apache.kafka.connect.connector.ConnectRecord;
import org.apache.kafka.connect.errors.DataException;
import org.apache.kafka.connect.header.Header;
import org.apache.kafka.connect.transforms.Transformation;

/** Pure sink guard. Errors expose a stable cause code, never traffic or identifiers. */
public final class SessionContract<R extends ConnectRecord<R>> implements Transformation<R> {
    private static final Set<String> IDENTITY = Set.of("schemaVersion","operation","documentKey","revision","siteId","sensorId","eventId");
    private static final Set<String> UPSERT = Set.of("schemaVersion","operation","documentKey","revision","siteId","sensorId","eventId",
        "startedAt","endedAt","sourceIp","destinationIp","sourcePort","destinationPort","protocol","vlanId","provenance","inferred",
        "partial","closeReason","packetCount","byteCount","acceptedAt");

    public R apply(R record) {
        if (!(record.value() instanceof Map<?,?> value)) throw failure("contract-value");
        if (!integer(value.get("schemaVersion"),1,1)) throw failure("contract-version");
        String operation = text(value,"operation");
        if (!operation.equals("upsert") && !operation.equals("delete")) throw failure("contract-operation");
        if (!value.keySet().equals(operation.equals("delete") ? IDENTITY : UPSERT)) throw failure("contract-fields");
        String key = encode(text(value,"siteId"))+"."+encode(text(value,"sensorId"))+"."+encode(text(value,"eventId"));
        if (!key.equals(value.get("documentKey")) || !key.equals(record.key())) throw failure("contract-identity");
        if (!integer(value.get("revision"),1,Long.MAX_VALUE)) throw failure("contract-revision");
        Header header = record.headers().lastWithName("revision");
        if (header==null || !integer(header.value(),1,Long.MAX_VALUE)
            || ((Number)header.value()).longValue()!=((Number)value.get("revision")).longValue()) throw failure("contract-revision-header");
        if (operation.equals("delete")) return record;
        Instant start=instant(value,"startedAt"), end=instant(value,"endedAt");
        instant(value,"acceptedAt");
        if (end.isBefore(start)) throw failure("contract-time-order");
        address(value,"sourceIp"); address(value,"destinationIp");
        if (!integer(value.get("sourcePort"),0,65535)||!integer(value.get("destinationPort"),0,65535)) throw failure("contract-port");
        if (!Set.of("TCP","UDP").contains(value.get("protocol"))) throw failure("contract-protocol");
        if (value.get("vlanId")!=null&&!integer(value.get("vlanId"),0,4094)) throw failure("contract-vlan");
        if ("synthetic".equals(value.get("provenance"))) {
            for(String name:Set.of("inferred","partial","closeReason","packetCount","byteCount"))
                if(value.get(name)!=null) throw failure("contract-synthetic");
        } else if ("capture".equals(value.get("provenance"))) {
            if(!Boolean.TRUE.equals(value.get("inferred"))||!(value.get("partial") instanceof Boolean)
                ||!Set.of("inactivity","max-duration","shutdown","restart").contains(value.get("closeReason"))
                ||!integer(value.get("packetCount"),0,Long.MAX_VALUE)||!integer(value.get("byteCount"),0,Long.MAX_VALUE)) throw failure("contract-capture");
        } else throw failure("contract-provenance");
        return record;
    }
    private static String text(Map<?,?> value,String field) {
        if(!(value.get(field) instanceof String text)||text.isBlank()||text.length()>128) throw failure("contract-text");
        return text;
    }
    private static boolean integer(Object value,long minimum,long maximum) {
        return (value instanceof Byte||value instanceof Short||value instanceof Integer||value instanceof Long)
            &&((Number)value).longValue()>=minimum&&((Number)value).longValue()<=maximum;
    }
    private static Instant instant(Map<?,?> value,String field) {
        Object item=value.get(field);
        if(!(item instanceof String text)||!text.matches("\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(\\.\\d{1,3})?Z")) throw failure("contract-time");
        try { return Instant.parse(text); } catch(RuntimeException exception) { throw failure("contract-time"); }
    }
    private static void address(Map<?,?> value,String field) {
        Object item=value.get(field);
        if(!(item instanceof String text)||!text.matches("[0-9a-fA-F:.]+")) throw failure("contract-ip");
        if(!text.contains(":")) {
            String[] parts=text.split("\\.",-1);
            if(parts.length!=4) throw failure("contract-ip");
            for(String part:parts) {
                try { if(part.isEmpty()||part.length()>3||Integer.parseInt(part)>255) throw failure("contract-ip"); }
                catch(NumberFormatException exception) { throw failure("contract-ip"); }
            }
        }
        // Literal-only syntax above prevents DNS resolution at this boundary.
        try { InetAddress.getByName(text); } catch(Exception exception) { throw failure("contract-ip"); }
    }
    private static String encode(String text) { return Base64.getUrlEncoder().withoutPadding().encodeToString(text.getBytes(StandardCharsets.UTF_8)); }
    private static DataException failure(String code) { return new DataException(code); }
    public void configure(Map<String,?> configuration) { }
    public void close() { }
    public ConfigDef config() { return new ConfigDef(); }
}
