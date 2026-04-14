FROM golang:1.26-alpine AS builder
WORKDIR /build
COPY go.mod go.sum ./
RUN go mod download
COPY main.go .
RUN go build -o service .

FROM alpine:3.21
WORKDIR /app
COPY --from=builder /build/service .
EXPOSE 8084
ENV PORT=8084
CMD ["./service"]
