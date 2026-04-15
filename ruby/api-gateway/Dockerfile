FROM ruby:3.2-slim

WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY Gemfile ./
RUN bundle install --without development test
COPY app.rb ./

ENV PORT=8080
EXPOSE 8080
CMD ["ruby", "app.rb"]
