FROM ruby:3.2-slim

WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl build-essential libpq-dev && rm -rf /var/lib/apt/lists/*
COPY Gemfile ./
RUN bundle install --without development test
COPY app.rb ./

ENV PORT=8085
EXPOSE 8085
CMD ["ruby", "app.rb"]
