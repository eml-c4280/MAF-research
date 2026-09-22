# Adds this network's corporate root CA (see certs/README.md) to the official Ollama image, so
# `ollama pull` can reach registry.ollama.ai through TLS-inspecting proxies like Zscaler.
# Without this, pulling a model fails with:
#   tls: failed to verify certificate: x509: certificate signed by unknown authority
# On a normal network certs/ is empty and this is a no-op - safe to use everywhere.
FROM ollama/ollama:latest
COPY certs/ /usr/local/share/ca-certificates/
RUN update-ca-certificates
