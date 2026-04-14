FROM ruby:3.2-slim

WORKDIR /app
RUN apt-get update && apt-get install -y libpq-dev && rm -rf /var/lib/apt/lists/*
COPY Gemfile ./
RUN bundle install --without development test
COPY app.rb ./

ENV PORT=8084
EXPOSE 8084
CMD ["ruby", "app.rb"]
