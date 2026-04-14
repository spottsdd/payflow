FROM golang:1.26-alpine AS builder

WORKDIR /build
COPY go.mod ./
COPY main.go ./
RUN go build -o traffic-generator .

FROM alpine:3.21

WORKDIR /app
COPY --from=builder /build/traffic-generator .

ENV GATEWAY_URL=http://api-gateway:8080
ENV REQUESTS_PER_SECOND=10
ENV CONCURRENCY=5
ENV DURATION_SECONDS=0

CMD ["./traffic-generator"]
